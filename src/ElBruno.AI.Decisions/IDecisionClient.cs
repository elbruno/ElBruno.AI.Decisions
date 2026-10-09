namespace ElBruno.AI.Decisions;

/// <summary>A provider-neutral decision model client. Decision models return probabilities, not generated text.</summary>
public interface IDecisionClient
{
    /// <summary>Chooses exactly one option and returns the probability of every option.</summary>
    /// <param name="input">The situation or content to decide on.</param>
    /// <param name="instructions">The question or selection criteria.</param>
    /// <param name="options">Exact option labels mapped to optional descriptions.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<ChoiceDecision> ChooseAsync(
        string input,
        string instructions,
        IReadOnlyDictionary<string, string?> options,
        CancellationToken cancellationToken = default);

    /// <summary>Scores the input against 2-10 ordered rubric levels.</summary>
    /// <param name="input">The content to grade.</param>
    /// <param name="instructions">The grading question.</param>
    /// <param name="rubric">Ordered level descriptions, lowest first.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<ScoreDecision> ScoreAsync(
        string input,
        string instructions,
        IReadOnlyList<string> rubric,
        CancellationToken cancellationToken = default);

    /// <summary>Estimates the probability that a proposition is true for the input.</summary>
    /// <param name="input">The content to assess.</param>
    /// <param name="proposition">The statement to evaluate.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<AssessmentDecision> AssessAsync(
        string input,
        string proposition,
        CancellationToken cancellationToken = default);
}
