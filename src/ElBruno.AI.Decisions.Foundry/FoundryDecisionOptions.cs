namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Settings for a Microsoft-Decision-1 deployment on Microsoft Foundry.</summary>
public sealed class FoundryDecisionOptions
{
    /// <summary>Gets or sets the full scoring URL of the deployment.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets the API key. Read it from configuration or user secrets, never from source code.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the header that carries <see cref="ApiKey"/>; use Authorization to send it as a Bearer token.</summary>
    public string ApiKeyHeaderName { get; set; } = "api-key";

    /// <summary>Gets or sets the deployment or model name sent in the request body.</summary>
    public string Model { get; set; } = "microsoft-decision-1";

    /// <summary>Gets or sets the request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal string[] ValidationErrors()
    {
        var errors = new List<string>();
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Endpoint must be an absolute HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey)) errors.Add("ApiKey is required.");
        if (string.IsNullOrWhiteSpace(ApiKeyHeaderName)) errors.Add("ApiKeyHeaderName is required.");
        if (string.IsNullOrWhiteSpace(Model)) errors.Add("Model is required.");
        if (Timeout <= TimeSpan.Zero) errors.Add("Timeout must be positive.");
        return [.. errors];
    }
}
