using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ElBruno.AI.Decisions;

/// <summary>Settings for <see cref="DecisionRoutingChatClient"/>.</summary>
public sealed class DecisionRoutingOptions
{
    /// <summary>Gets the question the decision model answers, for example "Which model should handle this request?".</summary>
    public required string Instructions { get; init; }

    /// <summary>Gets optional descriptions per route label. Every registered route is offered as an option.</summary>
    public IReadOnlyDictionary<string, string?> Descriptions { get; init; } = new Dictionary<string, string?>();

    /// <summary>Gets the minimum accepted confidence, or null for none. Below it the fallback route is used.</summary>
    public double? MinimumConfidence { get; init; }

    /// <summary>Gets the route used when confidence is too low. When null, low confidence throws.</summary>
    public string? FallbackRoute { get; init; }
}

/// <summary>Picks one of several chat clients with a decision model, then forwards the request to it.</summary>
/// <remarks>The decision uses the last user message text. Chat clients are borrowed, not disposed.</remarks>
public sealed class DecisionRoutingChatClient : IChatClient
{
    private readonly IDecisionClient _decisions;
    private readonly IReadOnlyDictionary<string, IChatClient> _routes;
    private readonly DecisionRoutingOptions _options;

    /// <summary>Creates a router. At least two routes are required, because a decision needs alternatives.</summary>
    public DecisionRoutingChatClient(IDecisionClient decisions, IReadOnlyDictionary<string, IChatClient> routes, DecisionRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Instructions);
        if (routes.Count < 2) throw new ArgumentException("At least two routes are required.", nameof(routes));
        if (options.MinimumConfidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Minimum confidence must be within [0,1].");
        }

        _routes = new Dictionary<string, IChatClient>(routes, StringComparer.Ordinal);
        if (options.FallbackRoute is { } fallback && !_routes.ContainsKey(fallback))
        {
            throw new ArgumentException("The fallback route must be registered.", nameof(options));
        }

        _decisions = decisions;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatMessage[] snapshot = [.. messages];
        IChatClient route = await SelectAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return await route.GetResponseAsync(snapshot, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage[] snapshot = [.. messages];
        IChatClient route = await SelectAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await foreach (ChatResponseUpdate update in route.GetStreamingResponseAsync(snapshot, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private async Task<IChatClient> SelectAsync(ChatMessage[] messages, CancellationToken cancellationToken)
    {
        string input = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("The request needs a user message with text.", nameof(messages));

        var choices = _routes.Keys.ToDictionary(
            label => label, label => _options.Descriptions.TryGetValue(label, out string? d) ? d : null, StringComparer.Ordinal);
        ChoiceDecision decision = await _decisions.ChooseAsync(input, _options.Instructions, choices, cancellationToken).ConfigureAwait(false);

        string label = decision.Choice;
        if (_options.MinimumConfidence is { } minimum && decision.Confidence < minimum)
        {
            label = _options.FallbackRoute
                ?? throw new DecisionException($"The routing confidence {decision.Confidence:F3} is below {minimum:F3}.");
        }

        return _routes.TryGetValue(label, out IChatClient? route)
            ? route
            : throw new DecisionException($"The decision selected unregistered route '{label}'.");
    }
}
