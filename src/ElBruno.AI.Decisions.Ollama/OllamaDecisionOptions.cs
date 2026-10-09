namespace ElBruno.AI.Decisions.Ollama;

/// <summary>Settings for decisions on a local Ollama server.</summary>
public sealed class OllamaDecisionOptions
{
    /// <summary>Gets or sets the Ollama resource root or full System One invocation URL.</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:11434/");

    /// <summary>Gets or sets a local System One model name, for example <c>nimble</c>. General chat and cloud models are not supported.</summary>
    public string Model { get; set; } = "";

    /// <summary>Gets or sets the request timeout. The first call may include model loading time.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    internal string[] ValidationErrors()
    {
        var errors = new List<string>();
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https"))
        {
            errors.Add("Endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(Model)) errors.Add("Model is required.");
        if (Timeout <= TimeSpan.Zero) errors.Add("Timeout must be positive.");
        return [.. errors];
    }
}
