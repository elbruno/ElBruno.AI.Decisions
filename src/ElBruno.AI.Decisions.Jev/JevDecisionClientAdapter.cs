namespace ElBruno.AI.Decisions.Jev;

/// <summary>Adapts a Jev client to the provider-neutral <see cref="IDecisionClient"/>.</summary>
public sealed class JevDecisionClientAdapter : IDecisionClient
{
    private const string Key = "decision";
    private readonly IJevDecisionClient _client;

    /// <summary>Creates an adapter over a Jev client.</summary>
    public JevDecisionClientAdapter(IJevDecisionClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<ChoiceDecision> ChooseAsync(
        string input, string instructions, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Choice(input, instructions, options);
        var key = new JevQuestionKey<JevChoiceAnswer>(Key);
        var request = new JevDecisionRequest(input).WithQuestion(key, new JevChoiceQuestion(instructions, options));
        JevChoiceAnswer answer = (await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false)).GetAnswer(key);
        return new ChoiceDecision(answer.Choice, answer.Probabilities, answer.Confidence);
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var key = new JevQuestionKey<JevScoreAnswer>(Key);
        var request = new JevDecisionRequest(input).WithQuestion(key, new JevScoreQuestion(instructions, rubric));
        JevScoreAnswer answer = (await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false)).GetAnswer(key);
        double[] levels = new double[rubric.Count];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = answer.Probabilities.TryGetValue(i.ToString(System.Globalization.CultureInfo.InvariantCulture), out double p) ? p : 0;
        }

        return new ScoreDecision(levels);
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        var key = new JevQuestionKey<JevNoulAnswer>(Key);
        var request = new JevDecisionRequest(input).WithQuestion(key, new JevNoulQuestion(proposition));
        JevNoulAnswer answer = (await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false)).GetAnswer(key);
        return new AssessmentDecision(answer.Probability);
    }
}
