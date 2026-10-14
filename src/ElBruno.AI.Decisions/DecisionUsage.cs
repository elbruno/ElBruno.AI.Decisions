using System.Text.Json;

namespace ElBruno.AI.Decisions;

/// <summary>Provider-reported token counts for one request. Missing counts are unknown, not zero.</summary>
public sealed class DecisionUsage
{
    /// <summary>Creates usage metadata without estimating counts or deriving totals.</summary>
    public DecisionUsage(long? inputTokens = null, long? outputTokens = null, long? totalTokens = null,
        long? reasoningTokens = null, long? cachedTokens = null)
    {
        InputTokens = Count(inputTokens, nameof(inputTokens));
        OutputTokens = Count(outputTokens, nameof(outputTokens));
        TotalTokens = Count(totalTokens, nameof(totalTokens));
        ReasoningTokens = Count(reasoningTokens, nameof(reasoningTokens));
        CachedTokens = Count(cachedTokens, nameof(cachedTokens));
    }

    /// <summary>Gets the reported input token count.</summary>
    public long? InputTokens { get; }

    /// <summary>Gets the reported output token count.</summary>
    public long? OutputTokens { get; }

    /// <summary>Gets the reported total, never an SDK-computed sum of input and output.</summary>
    public long? TotalTokens { get; }

    /// <summary>Gets the reported reasoning token count, when available.</summary>
    public long? ReasoningTokens { get; }

    /// <summary>Gets the reported cached input token count, when available.</summary>
    public long? CachedTokens { get; }

    private static long? Count(long? value, string name) =>
        value is < 0 ? throw new ArgumentOutOfRangeException(name, "Token counts must be nonnegative.") : value;
}

internal static class DecisionUsageReader
{
    internal static DecisionUsage? Read(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind == JsonValueKind.Null) return null;
        Object(usage);
        long? input = Count(usage, "input_tokens");
        long? output = Count(usage, "output_tokens");
        long? total = Count(usage, "total_tokens");
        long? reasoning = Count(usage, "reasoning_tokens");
        long? cached = Count(usage, "cached_tokens");
        long? reasoningDetails = Detail(usage, "output_tokens_details", "reasoning_tokens");
        long? cachedDetails = Detail(usage, "input_tokens_details", "cached_tokens");
        return new DecisionUsage(input, output, total, reasoning ?? reasoningDetails, cached ?? cachedDetails);
    }

    private static long? Detail(JsonElement usage, string name, string count)
    {
        if (!usage.TryGetProperty(name, out JsonElement details) || details.ValueKind == JsonValueKind.Null) return null;
        Object(details);
        return Count(details, count);
    }

    private static long? Count(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out JsonElement count) || count.ValueKind == JsonValueKind.Null) return null;
        if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt64(out long value) || value < 0)
            throw new DecisionException("A reported token count is invalid.");
        return value;
    }

    private static void Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new DecisionException("Reported token usage must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new DecisionException("Reported token usage contains duplicate fields.");
        }
    }
}
