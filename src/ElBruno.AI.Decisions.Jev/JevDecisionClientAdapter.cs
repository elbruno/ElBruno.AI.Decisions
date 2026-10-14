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
        JevDecisionResponse response = await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        JevChoiceAnswer answer = response.GetAnswer(key);
        return new ChoiceDecision(answer.Choice, answer.Probabilities, answer.Confidence, Usage(response));
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var key = new JevQuestionKey<JevScoreAnswer>(Key);
        var request = new JevDecisionRequest(input).WithQuestion(key, new JevScoreQuestion(instructions, rubric));
        JevDecisionResponse response = await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        JevScoreAnswer answer = response.GetAnswer(key);
        double[] levels = new double[rubric.Count];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = answer.Probabilities.TryGetValue(i.ToString(System.Globalization.CultureInfo.InvariantCulture), out double p) ? p : 0;
        }

        return new ScoreDecision(levels, Usage(response));
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        var key = new JevQuestionKey<JevNoulAnswer>(Key);
        var request = new JevDecisionRequest(input).WithQuestion(key, new JevNoulQuestion(proposition));
        JevDecisionResponse response = await _client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        JevNoulAnswer answer = response.GetAnswer(key);
        return new AssessmentDecision(answer.Probability, Usage(response));
    }

    private static DecisionUsage? Usage(JevDecisionResponse response) => response.HasUsage
        ? new DecisionUsage(response.Usage.InputTokens, response.Usage.OutputTokens, response.Usage.TotalTokens,
            response.Usage.ReasoningTokens, response.Usage.CachedTokens)
        : null;
}
