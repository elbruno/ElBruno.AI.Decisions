using System.Collections.ObjectModel;

namespace ElBruno.AI.Decisions;

/// <summary>The result of choosing among labeled options.</summary>
public sealed class ChoiceDecision
{
    /// <summary>Creates a choice result. The probabilities are preserved as reported by the provider.</summary>
    public ChoiceDecision(string choice, IReadOnlyDictionary<string, double> probabilities, double? confidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choice);
        Probabilities = DecisionValidation.Distribution(probabilities);
        if (!Probabilities.ContainsKey(choice))
        {
            throw new ArgumentException("The selected label must be in the distribution.", nameof(choice));
        }

        Choice = choice;
        Confidence = confidence is { } c ? DecisionValidation.Probability(c, nameof(confidence)) : Probabilities[choice];
    }

    /// <summary>Gets the selected label.</summary>
    public string Choice { get; }

    /// <summary>Gets the probability of every option.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>Gets provider confidence; defaults to the selected option's probability.</summary>
    public double Confidence { get; }
}

/// <summary>The result of grading against an ordered rubric.</summary>
public sealed class ScoreDecision
{
    /// <summary>Creates a score result from a probability per zero-based rubric level.</summary>
    public ScoreDecision(IReadOnlyList<double> levelProbabilities)
    {
        ArgumentNullException.ThrowIfNull(levelProbabilities);
        if (levelProbabilities.Count < 2)
        {
            throw new ArgumentException("A score needs at least two levels.", nameof(levelProbabilities));
        }

        LevelProbabilities = Array.AsReadOnly(levelProbabilities
            .Select(p => DecisionValidation.Probability(p, nameof(levelProbabilities))).ToArray());
        double total = LevelProbabilities.Sum();
        Score = total > 0 ? LevelProbabilities.Select((p, i) => p * i).Sum() / total : 0;
    }

    /// <summary>Gets the expected zero-based rubric position, not rounded.</summary>
    public double Score { get; }

    /// <summary>Gets the probability of each rubric level.</summary>
    public IReadOnlyList<double> LevelProbabilities { get; }
}

/// <summary>The probability that a proposition is true. It is intentionally not thresholded.</summary>
public sealed class AssessmentDecision
{
    /// <summary>Creates an assessment in [0,1].</summary>
    public AssessmentDecision(double probability) =>
        Probability = DecisionValidation.Probability(probability, nameof(probability));

    /// <summary>Gets the probability that the proposition is true.</summary>
    public double Probability { get; }
}

internal static class DecisionValidation
{
    internal static double Probability(double value, string parameterName) =>
        double.IsFinite(value) && value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "A probability must be finite and within [0,1].");

    internal static ReadOnlyDictionary<string, double> Distribution(IReadOnlyDictionary<string, double> probabilities)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        if (probabilities.Count == 0)
        {
            throw new ArgumentException("A distribution must not be empty.", nameof(probabilities));
        }

        return new ReadOnlyDictionary<string, double>(probabilities.ToDictionary(
            pair => pair.Key, pair => Probability(pair.Value, nameof(probabilities)), StringComparer.Ordinal));
    }
}
