namespace ElBruno.AI.Decisions;

/// <summary>Shared argument validation for provider implementations.</summary>
public static class DecisionRequestValidation
{
    /// <summary>Validates arguments for <see cref="IDecisionClient.ChooseAsync"/>.</summary>
    public static void Choice(string input, string instructions, IReadOnlyDictionary<string, string?> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count is < 2 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Choice requires 2-255 options.");
        }

        foreach (string label in options.Keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }
    }

    /// <summary>Validates arguments for <see cref="IDecisionClient.ScoreAsync"/>.</summary>
    public static void Score(string input, string instructions, IReadOnlyList<string> rubric)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(rubric);
        if (rubric.Count is < 2 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rubric), "Score requires 2-10 ordered levels.");
        }

        foreach (string level in rubric)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(level);
        }
    }

    /// <summary>Validates arguments for <see cref="IDecisionClient.AssessAsync"/>.</summary>
    public static void Assess(string input, string proposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposition);
    }
}
