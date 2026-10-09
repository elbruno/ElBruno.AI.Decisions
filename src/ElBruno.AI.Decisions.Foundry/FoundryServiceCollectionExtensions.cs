using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Registers the Foundry decision provider.</summary>
public static class FoundryServiceCollectionExtensions
{
    /// <summary>The named HTTP client used by the provider.</summary>
    public const string HttpClientName = "ElBruno.AI.Decisions.Foundry";

    /// <summary>Registers <see cref="IDecisionClient"/> backed by Microsoft-Decision-1 on Foundry.</summary>
    public static IHttpClientBuilder AddFoundryDecisions(this IServiceCollection services, Action<FoundryDecisionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new FoundryDecisionOptions();
        configure(options);
        services.TryAddSingleton<IDecisionClient>(provider => new FoundryDecisionClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options));
        return services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    }
}
