using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElBruno.AI.Decisions.Ollama;

/// <summary>Registers the Ollama decision provider.</summary>
public static class OllamaServiceCollectionExtensions
{
    /// <summary>The named HTTP client used by the provider.</summary>
    public const string HttpClientName = "ElBruno.AI.Decisions.Ollama";

    /// <summary>Registers <see cref="IDecisionClient"/> backed by a local Ollama model.</summary>
    public static IHttpClientBuilder AddOllamaDecisions(this IServiceCollection services, Action<OllamaDecisionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new OllamaDecisionOptions();
        configure(options);
        services.TryAddSingleton<IDecisionClient>(provider => new OllamaDecisionClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options));
        return services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    }
}
