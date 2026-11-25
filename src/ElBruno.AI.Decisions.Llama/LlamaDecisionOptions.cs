namespace ElBruno.AI.Decisions.Llama;

/// <summary>Configuration for a Llama App decision server.</summary>
public sealed class LlamaDecisionOptions
{
    /// <summary>Server root or full System One endpoint.</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:9931/");

    /// <summary>Model ID, or null to use the server's selected model.</summary>
    public string? Model { get; set; }

    /// <summary>Request timeout, including model loading.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}
