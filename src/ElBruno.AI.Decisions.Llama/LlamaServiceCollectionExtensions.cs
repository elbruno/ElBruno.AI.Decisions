using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElBruno.AI.Decisions.Llama;

/// <summary>Dependency injection for Llama decisions.</summary>
public static class LlamaServiceCollectionExtensions
{
    /// <summary>The HTTP client factory name.</summary>
    public const string HttpClientName = "ElBruno.AI.Decisions.Llama";

    /// <summary>Registers the concrete batch client and the shared decision interface.</summary>
    public static IHttpClientBuilder AddLlamaDecisions(this IServiceCollection services, Action<LlamaDecisionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new LlamaDecisionOptions();
        configure(options);
        services.TryAddSingleton(provider => new LlamaDecisionClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options, ownsHttp: true));
        services.TryAddSingleton<IDecisionClient>(provider => provider.GetRequiredService<LlamaDecisionClient>());
        return services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    }
}
