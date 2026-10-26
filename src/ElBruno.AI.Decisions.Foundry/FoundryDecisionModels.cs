using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Input evidence for one Azure OpenAI Decisions request.</summary>
public sealed class FoundryDecisionInput
{
    private readonly JsonNode _value;

    private FoundryDecisionInput(JsonNode value) => _value = value;

    /// <summary>Creates a request input from plain text.</summary>
    public static FoundryDecisionInput FromText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new FoundryDecisionInput(JsonValue.Create(text)!);
    }

    /// <summary>Creates request input from one or more user messages containing text and/or images.</summary>
    public static FoundryDecisionInput FromMessages(IEnumerable<FoundryDecisionMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        FoundryDecisionMessage[] snapshot = messages.ToArray();
        if (snapshot.Length == 0) throw new ArgumentException("At least one message is required.", nameof(messages));
        if (snapshot.Any(message => message is null))
            throw new ArgumentException("Messages must not contain null values.", nameof(messages));
        if (snapshot.Sum(message => message.ImageCount) > 128)
            throw new ArgumentOutOfRangeException(nameof(messages), "An input can contain at most 128 images.");
        return new FoundryDecisionInput(new JsonArray(snapshot.Select(message => (JsonNode?)message.ToJson()).ToArray()));
    }

    internal JsonNode ToJsonNode() => _value.DeepClone();
}

/// <summary>A user message for a text and/or image decision input.</summary>
public sealed class FoundryDecisionMessage
{
    private readonly FoundryDecisionContentPart[] _content;

    private FoundryDecisionMessage(IEnumerable<FoundryDecisionContentPart> content)
    {
        _content = content.ToArray();
        if (_content.Length == 0) throw new ArgumentException("A message must contain at least one content part.", nameof(content));
        if (_content.Any(part => part is null))
            throw new ArgumentException("Message content must not contain null values.", nameof(content));
    }

    internal int ImageCount => _content.Count(part => part.Type == "input_image");

    /// <summary>Creates a user message with mixed text and image content.</summary>
    public static FoundryDecisionMessage User(IEnumerable<FoundryDecisionContentPart> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new FoundryDecisionMessage(content);
    }

    internal JsonObject ToJson() => new()
    {
        ["role"] = "user",
        ["content"] = new JsonArray(_content.Select(part => (JsonNode?)part.ToJson()).ToArray())
    };
}

/// <summary>A text or image part in a user message.</summary>
public sealed class FoundryDecisionContentPart
{
    private FoundryDecisionContentPart(string type, string value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Gets the content part type.</summary>
    public string Type { get; }

    /// <summary>Gets the text or image URL/data URL.</summary>
    public string Value { get; }

    /// <summary>Creates a text content part.</summary>
    public static FoundryDecisionContentPart Text(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new FoundryDecisionContentPart("input_text", text);
    }

    /// <summary>Creates an image content part from an HTTPS URL or image data URL.</summary>
    public static FoundryDecisionContentPart Image(string imageUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        bool isHttpsUrl = Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps;
        int comma = imageUrl.IndexOf(',');
        bool isImageDataUrl = imageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && comma > 10;
        if (isImageDataUrl)
        {
            int parameter = imageUrl.IndexOf(';');
            int mediaTypeEnd = parameter >= 0 && parameter < comma ? parameter : comma;
            string mediaType = imageUrl[5..mediaTypeEnd];
            isImageDataUrl = mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);
        }
        if (!isHttpsUrl && !isImageDataUrl)
            throw new ArgumentException("An image must use an HTTPS URL or image data URL.", nameof(imageUrl));
        return new FoundryDecisionContentPart("input_image", imageUrl);
    }

    internal JsonObject ToJson() => Type == "input_text"
        ? new JsonObject { ["type"] = Type, ["text"] = Value }
        : new JsonObject { ["type"] = Type, ["image_url"] = Value };
}

/// <summary>A named question in an Azure OpenAI Decisions request.</summary>
public abstract class FoundryDecisionQuestion
{
    /// <summary>Creates a named decision question.</summary>
    /// <param name="name">Unique question name.</param>
    /// <param name="instructions">Instructions for the decision.</param>
    protected FoundryDecisionQuestion(string name, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        Name = name;
        Instructions = instructions;
    }

    /// <summary>Gets the unique name used to correlate this question with its answer.</summary>
    public string Name { get; }

    /// <summary>Gets the decision instructions.</summary>
    public string Instructions { get; }

    internal abstract string Type { get; }
    internal abstract JsonObject ToJson();

    internal JsonObject BaseJson() => new()
    {
        ["type"] = Type,
        ["name"] = Name,
        ["instructions"] = Instructions
    };
}

/// <summary>Checks whether a condition is true.</summary>
public sealed class FoundryPredicateQuestion(string name, string instructions)
    : FoundryDecisionQuestion(name, instructions)
{
    internal override string Type => "predicate";
    internal override JsonObject ToJson() => BaseJson();
}

/// <summary>A choice value and its optional explanatory description.</summary>
public sealed record FoundryDecisionChoice
{
    /// <summary>Creates a choice value.</summary>
    public FoundryDecisionChoice(string value, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
        Description = description;
    }

    /// <summary>Gets the exact value returned for the selected choice.</summary>
    public string Value { get; }

    /// <summary>Gets an optional description that clarifies when the choice applies.</summary>
    public string? Description { get; }

    internal JsonObject ToJson()
    {
        var result = new JsonObject { ["value"] = Value };
        if (Description is not null) result["description"] = Description;
        return result;
    }
}

/// <summary>Selects one value from a fixed set of options.</summary>
public sealed class FoundryChoiceQuestion : FoundryDecisionQuestion
{
    private readonly FoundryDecisionChoice[] _choices;

    /// <summary>Creates a choice question.</summary>
    public FoundryChoiceQuestion(string name, string instructions, IEnumerable<FoundryDecisionChoice> choices)
        : base(name, instructions)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _choices = choices.ToArray();
        if (_choices.Length < 2) throw new ArgumentException("A choice question needs at least two options.", nameof(choices));
        if (_choices.Any(choice => choice is null)) throw new ArgumentException("Choices must not contain null values.", nameof(choices));
        if (_choices.Select(choice => choice.Value).Distinct(StringComparer.Ordinal).Count() != _choices.Length)
            throw new ArgumentException("Choice values must be unique.", nameof(choices));
    }

    /// <summary>Gets the choices in request order.</summary>
    public IReadOnlyList<FoundryDecisionChoice> Choices => Array.AsReadOnly(_choices);

    internal override string Type => "choice";
    internal override JsonObject ToJson()
    {
        JsonObject result = BaseJson();
        result["choices"] = new JsonArray(_choices.Select(choice => (JsonNode?)choice.ToJson()).ToArray());
        return result;
    }
}

/// <summary>An ordered score level and its optional description.</summary>
public sealed record FoundryDecisionLevel
{
    /// <summary>Creates a score level.</summary>
    public FoundryDecisionLevel(string label, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
        Description = description;
    }

    /// <summary>Gets the level label.</summary>
    public string Label { get; }

    /// <summary>Gets an optional description of the level.</summary>
    public string? Description { get; }

    internal JsonObject ToJson()
    {
        var result = new JsonObject { ["label"] = Label };
        if (Description is not null) result["description"] = Description;
        return result;
    }
}

/// <summary>Scores an input against ordered levels.</summary>
public sealed class FoundryScoreQuestion : FoundryDecisionQuestion
{
    private readonly FoundryDecisionLevel[] _levels;

    /// <summary>Creates a score question.</summary>
    public FoundryScoreQuestion(string name, string instructions, IEnumerable<FoundryDecisionLevel> levels)
        : base(name, instructions)
    {
        ArgumentNullException.ThrowIfNull(levels);
        _levels = levels.ToArray();
        if (_levels.Length < 2) throw new ArgumentException("A score question needs at least two levels.", nameof(levels));
        if (_levels.Any(level => level is null)) throw new ArgumentException("Levels must not contain null values.", nameof(levels));
    }

    /// <summary>Gets the levels in ascending score order.</summary>
    public IReadOnlyList<FoundryDecisionLevel> Levels => Array.AsReadOnly(_levels);

    internal override string Type => "score";
    internal override JsonObject ToJson()
    {
        JsonObject result = BaseJson();
        result["levels"] = new JsonArray(_levels.Select(level => (JsonNode?)level.ToJson()).ToArray());
        return result;
    }
}

/// <summary>The result of one Azure OpenAI Decisions request.</summary>
public sealed class FoundryDecisionResult
{
    internal FoundryDecisionResult(string? model, IReadOnlyList<FoundryDecisionAnswer> answers, FoundryDecisionUsage? usage)
    {
        Model = model;
        Answers = Array.AsReadOnly(answers.ToArray());
        Usage = usage;
    }

    /// <summary>Gets the model identifier returned by the service, when present.</summary>
    public string? Model { get; }

    /// <summary>Gets answers correlated to the request questions, in request order.</summary>
    public IReadOnlyList<FoundryDecisionAnswer> Answers { get; }

    /// <summary>Gets token usage returned by the service, when present.</summary>
    public FoundryDecisionUsage? Usage { get; }
}

/// <summary>A typed answer to a named Foundry decision question.</summary>
public abstract class FoundryDecisionAnswer
{
    internal FoundryDecisionAnswer(string name, string type)
    {
        Name = name;
        Type = type;
    }

    /// <summary>Gets the question name.</summary>
    public string Name { get; }

    /// <summary>Gets the answer type.</summary>
    public string Type { get; }
}

/// <summary>The probability that a predicate is true.</summary>
public sealed class FoundryPredicateAnswer(string name, double probability)
    : FoundryDecisionAnswer(name, "predicate")
{
    /// <summary>Gets the predicate probability in [0,1].</summary>
    public double Probability { get; } = FoundryDecisionValidation.Probability(probability, nameof(probability));
}

/// <summary>The probability reported for one choice value.</summary>
public sealed record FoundryChoiceProbability(string Value, double Probability);

/// <summary>The selected option and its probability distribution.</summary>
public sealed class FoundryChoiceAnswer : FoundryDecisionAnswer
{
    internal FoundryChoiceAnswer(string name, string choice, IReadOnlyDictionary<string, double> probabilities, double confidence)
        : base(name, "choice")
    {
        Choice = choice;
        Probabilities = new ReadOnlyDictionary<string, double>(probabilities.ToDictionary(
            item => item.Key, item => FoundryDecisionValidation.Probability(item.Value, nameof(probabilities)), StringComparer.Ordinal));
        Confidence = FoundryDecisionValidation.Probability(confidence, nameof(confidence));
    }

    /// <summary>Gets the selected value.</summary>
    public string Choice { get; }

    /// <summary>Gets the probability for each choice value.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>Gets the provider's distinct confidence value.</summary>
    public double Confidence { get; }
}

/// <summary>The probability reported for one ordered score level.</summary>
public sealed record FoundryScoreProbability(int Value, string Label, double Probability);

/// <summary>The expected score and probability distribution over ordered levels.</summary>
public sealed class FoundryScoreAnswer : FoundryDecisionAnswer
{
    internal FoundryScoreAnswer(string name, double score, IReadOnlyList<FoundryScoreProbability> probabilities, double confidence)
        : base(name, "score")
    {
        Score = score;
        Probabilities = Array.AsReadOnly(probabilities.Select(probability =>
            new FoundryScoreProbability(probability.Value, probability.Label,
                FoundryDecisionValidation.Probability(probability.Probability, nameof(probabilities)))).ToArray());
        Confidence = FoundryDecisionValidation.Probability(confidence, nameof(confidence));
    }

    /// <summary>Gets the probability-weighted average of zero-based level values.</summary>
    public double Score { get; }

    /// <summary>Gets the probability for each score level.</summary>
    public IReadOnlyList<FoundryScoreProbability> Probabilities { get; }

    /// <summary>Gets the provider's distinct confidence value.</summary>
    public double Confidence { get; }
}

/// <summary>A refusal to answer one named question.</summary>
public sealed class FoundryRefusalAnswer(string name)
    : FoundryDecisionAnswer(name, "refusal");

/// <summary>Token usage returned for a Decisions request.</summary>
public sealed class FoundryDecisionUsage(int inputTokens, int outputTokens, int totalTokens)
{
    /// <summary>Gets the input token count.</summary>
    public int InputTokens { get; } = inputTokens;

    /// <summary>Gets the output token count.</summary>
    public int OutputTokens { get; } = outputTokens;

    /// <summary>Gets the total token count.</summary>
    public int TotalTokens { get; } = totalTokens;
}

internal static class FoundryDecisionValidation
{
    internal static double Probability(double value, string parameterName) =>
        double.IsFinite(value) && value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "A probability must be finite and within [0,1].");
}
