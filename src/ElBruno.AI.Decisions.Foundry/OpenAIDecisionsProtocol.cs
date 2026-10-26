using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry;

internal static class OpenAIDecisionsProtocol
{
    private const double ProbabilityTolerance = 1e-6;

    internal static JsonObject CreateRequest(
        string model, FoundryDecisionInput input, IReadOnlyList<FoundryDecisionQuestion> questions) => new()
        {
            ["model"] = model,
            ["input"] = input.ToJsonNode(),
            ["questions"] = new JsonArray(questions.Select(question => (JsonNode?)question.ToJson()).ToArray())
        };

    internal static FoundryDecisionResult ParseResponse(string body, IReadOnlyList<FoundryDecisionQuestion> questions)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("answers", out JsonElement answers) ||
                answers.ValueKind != JsonValueKind.Array || answers.GetArrayLength() != questions.Count)
                throw new DecisionException("The Azure OpenAI response must contain one answer per requested question.");

            Dictionary<string, FoundryDecisionQuestion> questionsByName =
                questions.ToDictionary(question => question.Name, StringComparer.Ordinal);
            var answersByName = new Dictionary<string, FoundryDecisionAnswer>(StringComparer.Ordinal);
            foreach (JsonElement answer in answers.EnumerateArray())
            {
                if (answer.ValueKind != JsonValueKind.Object)
                    throw new DecisionException("The Azure OpenAI decision answer must be an object.");
                string name = ReadString(answer, "name");
                if (!questionsByName.TryGetValue(name, out FoundryDecisionQuestion? question) ||
                    answersByName.ContainsKey(name))
                    throw new DecisionException("The Azure OpenAI response contains an unknown or duplicate question name.");
                answersByName.Add(name, ParseAnswer(answer, question));
            }

            string? model = OptionalString(root, "model");
            FoundryDecisionUsage? usage = ParseUsage(root);
            return new FoundryDecisionResult(model, questions.Select(question => answersByName[question.Name]).ToArray(), usage);
        }
        catch (JsonException ex)
        {
            throw new DecisionException("The Azure OpenAI Decisions response was not valid JSON.", ex);
        }
    }

    internal static JsonElement ToSystemOneAnswer(FoundryDecisionAnswer answer)
    {
        var normalized = new JsonObject();
        switch (answer)
        {
            case FoundryPredicateAnswer predicate:
                normalized["type"] = "noul";
                normalized["noul"] = predicate.Probability;
                break;
            case FoundryChoiceAnswer choice:
                normalized["type"] = "choice";
                normalized["choice"] = choice.Choice;
                normalized["confidence"] = choice.Confidence;
                normalized["probabilities"] = new JsonObject(choice.Probabilities.Select(item =>
                    new KeyValuePair<string, JsonNode?>(item.Key, JsonValue.Create(item.Value))));
                break;
            case FoundryScoreAnswer score:
                normalized["type"] = "score";
                normalized["score"] = score.Score;
                normalized["confidence"] = score.Confidence;
                normalized["probabilities"] = new JsonObject(score.Probabilities.Select(item =>
                    new KeyValuePair<string, JsonNode?>(
                        item.Value.ToString(CultureInfo.InvariantCulture), JsonValue.Create(item.Probability))));
                break;
            case FoundryRefusalAnswer:
                throw new DecisionException("The decision model refused the request.");
            default:
                throw new DecisionException("The Azure OpenAI Decisions response has an unsupported answer type.");
        }

        using JsonDocument document = JsonDocument.Parse(normalized.ToJsonString());
        return document.RootElement.Clone();
    }

    private static FoundryDecisionAnswer ParseAnswer(JsonElement answer, FoundryDecisionQuestion question)
    {
        string type = ReadString(answer, "type");
        if (type == "refusal") return new FoundryRefusalAnswer(question.Name);
        if (type != question.Type)
            throw new DecisionException("The Azure OpenAI answer type does not match its question.");

        return question switch
        {
            FoundryPredicateQuestion => new FoundryPredicateAnswer(question.Name, ReadProbability(answer, "probability")),
            FoundryChoiceQuestion choiceQuestion => ParseChoice(answer, choiceQuestion),
            FoundryScoreQuestion scoreQuestion => ParseScore(answer, scoreQuestion),
            _ => throw new DecisionException("The Azure OpenAI question type is unsupported.")
        };
    }

    private static FoundryChoiceAnswer ParseChoice(JsonElement answer, FoundryChoiceQuestion question)
    {
        string choice = ReadString(answer, "choice");
        if (!question.Choices.Any(option => string.Equals(option.Value, choice, StringComparison.Ordinal)))
            throw new DecisionException("The Azure OpenAI choice is not one of the supplied values.");
        Dictionary<string, double> probabilities = ParseProbabilities(answer, question.Choices.Select(option => option.Value).ToArray());
        return new FoundryChoiceAnswer(question.Name, choice, probabilities, ReadProbability(answer, "confidence"));
    }

    private static FoundryScoreAnswer ParseScore(JsonElement answer, FoundryScoreQuestion question)
    {
        JsonElement probabilitiesElement = ReadProbabilitiesArray(answer);
        var probabilities = new Dictionary<int, FoundryScoreProbability>();
        foreach (JsonElement entry in probabilitiesElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("value", out JsonElement valueElement) ||
                !valueElement.TryGetInt32(out int value) ||
                !entry.TryGetProperty("label", out JsonElement labelElement) ||
                labelElement.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("probability", out JsonElement probabilityElement) ||
                probabilityElement.ValueKind != JsonValueKind.Number ||
                !probabilityElement.TryGetDouble(out double probability) ||
                value < 0 || value >= question.Levels.Count ||
                !probabilities.TryAdd(value, new FoundryScoreProbability(
                    value, labelElement.GetString()!, probability)))
                throw new DecisionException("Invalid Azure OpenAI score probability entry.");
        }

        if (probabilities.Count != question.Levels.Count)
            throw new DecisionException("The Azure OpenAI score probabilities do not cover every level.");
        var ordered = Enumerable.Range(0, question.Levels.Count).Select(index =>
        {
            FoundryScoreProbability probability = probabilities[index];
            if (!string.Equals(probability.Label, question.Levels[index].Label, StringComparison.Ordinal))
                throw new DecisionException("The Azure OpenAI score label does not match the requested level.");
            return probability;
        }).ToArray();
        ValidateProbabilitySum(ordered.Select(item => item.Probability));
        double score = ReadFiniteNumber(answer, "score");
        double expected = ordered.Sum(item => item.Value * item.Probability);
        if (Math.Abs(score - expected) > ProbabilityTolerance)
            throw new DecisionException("The Azure OpenAI score does not match its level probabilities.");
        return new FoundryScoreAnswer(question.Name, score, ordered, ReadProbability(answer, "confidence"));
    }

    private static Dictionary<string, double> ParseProbabilities(JsonElement answer, string[] expectedValues)
    {
        JsonElement entries = ReadProbabilitiesArray(answer);
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("value", out JsonElement valueElement) ||
                valueElement.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("probability", out JsonElement probabilityElement) ||
                probabilityElement.ValueKind != JsonValueKind.Number ||
                !probabilityElement.TryGetDouble(out double probability))
                throw new DecisionException("Invalid Azure OpenAI choice probability entry.");
            string value = valueElement.GetString()!;
            if (!expectedValues.Contains(value, StringComparer.Ordinal) || !result.TryAdd(value, probability))
                throw new DecisionException("Unexpected or duplicate Azure OpenAI choice probability value.");
        }

        if (result.Count != expectedValues.Length)
            throw new DecisionException("The Azure OpenAI choice probabilities do not cover every option.");
        ValidateProbabilitySum(result.Values);
        return result;
    }

    private static JsonElement ReadProbabilitiesArray(JsonElement answer) =>
        answer.TryGetProperty("probabilities", out JsonElement probabilities) &&
        probabilities.ValueKind == JsonValueKind.Array
            ? probabilities
            : throw new DecisionException("Missing Azure OpenAI decision probability distribution.");

    private static void ValidateProbabilitySum(IEnumerable<double> values)
    {
        double[] probabilities = values.ToArray();
        if (probabilities.Any(value => !double.IsFinite(value) || value is < 0 or > 1) ||
            Math.Abs(probabilities.Sum() - 1) > ProbabilityTolerance)
            throw new DecisionException("The Azure OpenAI decision probabilities must be valid and sum to one.");
    }

    private static FoundryDecisionUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind == JsonValueKind.Null)
            return null;
        if (usage.ValueKind != JsonValueKind.Object ||
            !ReadNonnegativeInt(usage, "input_tokens", out int input) ||
            !ReadNonnegativeInt(usage, "output_tokens", out int output) ||
            !ReadNonnegativeInt(usage, "total_tokens", out int total))
            throw new DecisionException("The Azure OpenAI usage data is invalid.");
        return new FoundryDecisionUsage(input, output, total);
    }

    private static bool ReadNonnegativeInt(JsonElement element, string property, out int result)
    {
        result = 0;
        return element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out result) &&
            result >= 0;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new DecisionException($"Missing or invalid Azure OpenAI decision field '{property}'.");

    private static string? OptionalString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Null) return null;
        throw new DecisionException($"The Azure OpenAI response field '{property}' must be a string.");
    }

    private static double ReadProbability(JsonElement element, string property)
    {
        double value = ReadFiniteNumber(element, property);
        if (value is < 0 or > 1)
            throw new DecisionException($"The Azure OpenAI probability '{property}' is outside [0,1].");
        return value;
    }

    private static double ReadFiniteNumber(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number) &&
        double.IsFinite(number)
            ? number
            : throw new DecisionException($"Missing or invalid Azure OpenAI decision number '{property}'.");
}
