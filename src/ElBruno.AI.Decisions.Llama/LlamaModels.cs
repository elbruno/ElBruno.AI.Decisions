namespace ElBruno.AI.Decisions.Llama;

/// <summary>A named question in a batch.</summary>
public abstract record LlamaQuestion(string Instructions);

/// <summary>A multiple-choice question.</summary>
public sealed record LlamaChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string?> Criteria) : LlamaQuestion(Instructions);

/// <summary>A question with ordered levels, from lowest to highest.</summary>
public sealed record LlamaScoreQuestion(string Instructions, IReadOnlyList<string> Criteria) : LlamaQuestion(Instructions);

/// <summary>A yes/no question returning the probability of yes.</summary>
public sealed record LlamaNoulQuestion(string Instructions) : LlamaQuestion(Instructions);

/// <summary>Image or audio bytes and their MIME type.</summary>
public sealed record LlamaMedia(string MediaType, byte[] Data);

/// <summary>A batch sharing a text state and optional media.</summary>
public sealed record LlamaDecisionRequest(string State, IReadOnlyDictionary<string, LlamaQuestion> Questions)
{
    /// <summary>Images included in the state.</summary>
    public IReadOnlyList<LlamaMedia> Images { get; init; } = [];

    /// <summary>Audio files included in the state.</summary>
    public IReadOnlyList<LlamaMedia> Audio { get; init; } = [];
}

/// <summary>A single named batch answer.</summary>
public abstract record LlamaAnswer;

/// <summary>A choice and its provider probabilities and confidence.</summary>
public sealed record LlamaChoiceAnswer(string Choice, IReadOnlyDictionary<string, double> Probabilities, double? Confidence) : LlamaAnswer;

/// <summary>The provider's score, rubric distribution and confidence.</summary>
public sealed record LlamaScoreAnswer(double Score, IReadOnlyList<double> Probabilities, double? Confidence) : LlamaAnswer;

/// <summary>The provider's probability of yes.</summary>
public sealed record LlamaNoulAnswer(double Probability) : LlamaAnswer;

/// <summary>Answers correlated by question name, with provider metadata.</summary>
public sealed record LlamaDecisionResponse(string? Model, IReadOnlyDictionary<string, LlamaAnswer> Answers, DecisionUsage? Usage);
