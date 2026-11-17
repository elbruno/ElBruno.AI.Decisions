using System.Text.Json;
using Microsoft.AI.Foundry.Local;

namespace ElBruno.AI.Decisions.Foundry.Local;

/// <summary>Runs provider-neutral decisions with a loaded typed-decision model from Foundry Local.</summary>
/// <remarks>
/// The client does not own the model or the Foundry Local manager. It loads the model on the first
/// request when necessary; unload the model and dispose the manager when the application is finished.
/// </remarks>
public sealed class FoundryLocalDecisionClient : IDecisionClient
{
    private const string QuestionId = "decision";
    private const double ProbabilityTolerance = 1e-6;
    private readonly Func<DecisionRequest, CancellationToken, Task<DecisionResult>> _evaluate;

    /// <summary>Creates a client over a model registered with task <c>typed-decision</c>.</summary>
    /// <param name="model">The Foundry Local model handle. Its manager must remain alive while this client is used.</param>
    public FoundryLocalDecisionClient(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Info.Task != "typed-decision")
            throw new ArgumentException("The model must be registered with task 'typed-decision'.", nameof(model));
        _evaluate = CreateEvaluator(model);
    }

    internal FoundryLocalDecisionClient(Func<DecisionRequest, CancellationToken, Task<DecisionResult>> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        _evaluate = evaluate;
    }

    /// <inheritdoc />
    public async Task<ChoiceDecision> ChooseAsync(
        string input,
        string instructions,
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Choice(input, instructions, options);
        if (options.Count > 255)
            throw new ArgumentOutOfRangeException(nameof(options), "Foundry Local typed decisions support at most 255 choice criteria.");

        var question = DecisionQuestion.Choice(
            JsonSerializer.SerializeToElement(options),
            JsonSerializer.SerializeToElement(instructions));
        DecisionAnswer answer = await GetAnswerAsync(input, question, cancellationToken).ConfigureAwait(false);
        if (answer.Type != DecisionQuestionKind.Choice || string.IsNullOrWhiteSpace(answer.Choice))
            throw new DecisionException("The Foundry Local choice answer does not match the requested question.");

        Dictionary<string, double> probabilities = ProbabilityDistribution(answer.Probabilities, options.Keys);
        return new ChoiceDecision(answer.Choice, probabilities, answer.Confidence);
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input,
        string instructions,
        IReadOnlyList<string> rubric,
        CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var question = DecisionQuestion.Score(
            JsonSerializer.SerializeToElement(rubric),
            JsonSerializer.SerializeToElement(instructions));
        DecisionAnswer answer = await GetAnswerAsync(input, question, cancellationToken).ConfigureAwait(false);
        if (answer.Type != DecisionQuestionKind.Score || answer.Score is not { } score || !double.IsFinite(score))
            throw new DecisionException("The Foundry Local score answer does not match the requested question.");

        string[] labels = Enumerable.Range(0, rubric.Count).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Dictionary<string, double> probabilities = ProbabilityDistribution(answer.Probabilities, labels);
        double[] ordered = labels.Select(label => probabilities[label]).ToArray();
        var result = new ScoreDecision(ordered);
        if (Math.Abs(result.Score - score) > ProbabilityTolerance)
            throw new DecisionException("The Foundry Local score does not match its level probabilities.");
        return result;
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(
        string input,
        string proposition,
        CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        var question = DecisionQuestion.Noul(JsonSerializer.SerializeToElement(proposition));
        DecisionAnswer answer = await GetAnswerAsync(input, question, cancellationToken).ConfigureAwait(false);
        if (answer.Type != DecisionQuestionKind.Noul || answer.Noul is not { } probability)
            throw new DecisionException("The Foundry Local noul answer does not match the requested question.");
        return new AssessmentDecision(probability);
    }

    private async Task<DecisionAnswer> GetAnswerAsync(
        string input,
        DecisionQuestion question,
        CancellationToken cancellationToken)
    {
        DecisionRequest request = new()
        {
            State = JsonSerializer.SerializeToElement(input),
            Questions = new Dictionary<string, DecisionQuestion>(StringComparer.Ordinal)
            {
                [QuestionId] = question
            }
        };
        DecisionResult result = await _evaluate(request, cancellationToken).ConfigureAwait(false);
        if (result.Answers is null || result.Answers.Count != 1 ||
            !result.Answers.TryGetValue(QuestionId, out DecisionAnswer? answer) || answer is null)
            throw new DecisionException("The Foundry Local response must contain exactly one answer for the requested decision.");
        return answer;
    }

    private static Func<DecisionRequest, CancellationToken, Task<DecisionResult>> CreateEvaluator(IModel model) =>
        async (request, cancellationToken) =>
    {
        if (!await model.IsLoadedAsync(cancellationToken).ConfigureAwait(false))
            await model.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var session = new DecisionSession(model);
        return await session.DecideAsync(request, cancellationToken).ConfigureAwait(false);
    };

    private static Dictionary<string, double> ProbabilityDistribution(
        IReadOnlyDictionary<string, double>? values,
        IEnumerable<string> expectedLabels)
    {
        string[] expected = expectedLabels.ToArray();
        if (values is null || values.Count != expected.Length ||
            expected.Any(label => !values.ContainsKey(label)))
            throw new DecisionException("The Foundry Local response is missing probabilities for one or more requested labels.");

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string label in expected)
        {
            double probability = values[label];
            if (!double.IsFinite(probability) || probability is < 0 or > 1)
                throw new DecisionException("The Foundry Local response contains an invalid probability.");
            result.Add(label, probability);
        }
        if (Math.Abs(result.Values.Sum() - 1) > ProbabilityTolerance)
            throw new DecisionException("The Foundry Local probabilities must sum to one.");
        return result;
    }
}
