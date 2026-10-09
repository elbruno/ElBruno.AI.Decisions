using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions;

internal static class SystemOneProtocol
{
    internal static JsonObject Question(string type, string instructions) => new()
    {
        ["type"] = type,
        ["instructions"] = instructions
    };

    internal static JsonElement Answer(string body, string expectedType)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("answers", out JsonElement answers) ||
                answers.ValueKind != JsonValueKind.Object || answers.EnumerateObject().Count() != 1 ||
                !answers.TryGetProperty("decision", out JsonElement answer))
                throw new DecisionException("Expected exactly one decision answer.");
            if (answer.ValueKind != JsonValueKind.Object)
                throw new DecisionException("The decision answer must be an object.");
            string type = String(answer, "type");
            if (type == "refusal") throw new DecisionException("The decision model refused the request.");
            if (type != expectedType)
                throw new DecisionException("The decision answer does not match the requested question.");
            return answer.Clone();
        }
        catch (JsonException ex)
        {
            throw new DecisionException("The System One response was not valid JSON.", ex);
        }
    }

    internal static string String(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new DecisionException($"Missing or invalid decision field '{property}'.");
        return value.GetString()!;
    }

    internal static double Probability(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out JsonElement node) || node.ValueKind != JsonValueKind.Number ||
            !node.TryGetDouble(out double value) || !double.IsFinite(value) || value is < 0 or > 1)
            throw new DecisionException($"Missing or invalid decision probability '{property}'.");
        return value;
    }

    internal static Dictionary<string, double> Distribution(JsonElement answer, string[] labels, double tolerance = 1e-6)
    {
        if (!answer.TryGetProperty("probabilities", out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
            throw new DecisionException("Missing decision probability distribution.");
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (JsonProperty entry in entries.EnumerateObject())
        {
            if (!labels.Contains(entry.Name, StringComparer.Ordinal) || !result.TryAdd(entry.Name, Probability(entries, entry.Name)))
                throw new DecisionException("Unexpected or duplicate decision option.");
        }
        if (result.Count != labels.Length || Math.Abs(result.Values.Sum() - 1) > tolerance)
            throw new DecisionException("The decision probabilities must cover all options and sum to one.");
        return result;
    }
}
