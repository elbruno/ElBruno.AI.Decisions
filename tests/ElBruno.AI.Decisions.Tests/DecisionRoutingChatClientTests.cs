using Microsoft.Extensions.AI;

namespace ElBruno.AI.Decisions.Tests;

public sealed class DecisionRoutingChatClientTests
{
    private sealed class FakeDecisions(string choice, double confidence = 1) : IDecisionClient
    {
        public string? Input { get; private set; }
        public IReadOnlyDictionary<string, string?>? Options { get; private set; }

        public Task<ChoiceDecision> ChooseAsync(string input, string instructions, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken = default)
        {
            Input = input;
            Options = options;
            return Task.FromResult(new ChoiceDecision(choice, options.Keys.ToDictionary(k => k, k => k == choice ? confidence : (1 - confidence) / (options.Count - 1)), confidence));
        }

        public Task<ScoreDecision> ScoreAsync(string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeChat(string name) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, name)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, name);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static DecisionRoutingOptions Options(double? min = null, string? fallback = null) => new()
    {
        Instructions = "Pick the model.",
        Descriptions = new Dictionary<string, string?> { ["fast"] = "Simple", ["deep"] = "Complex" },
        MinimumConfidence = min,
        FallbackRoute = fallback
    };

    [Fact]
    public async Task ForwardsToTheChosenRoute()
    {
        var fast = new FakeChat("fast");
        var deep = new FakeChat("deep");
        var decisions = new FakeDecisions("deep");
        var router = new DecisionRoutingChatClient(decisions, new Dictionary<string, IChatClient> { ["fast"] = fast, ["deep"] = deep }, Options());

        ChatResponse response = await router.GetResponseAsync([new ChatMessage(ChatRole.User, "prove the theorem")]);

        Assert.Equal("deep", response.Text);
        Assert.Equal(0, fast.Calls);
        Assert.Equal("prove the theorem", decisions.Input);
        Assert.Equal("Complex", decisions.Options!["deep"]);
    }

    [Fact]
    public async Task StreamsFromTheChosenRoute()
    {
        var router = new DecisionRoutingChatClient(new FakeDecisions("fast"),
            new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast"), ["deep"] = new FakeChat("deep") }, Options());

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate u in router.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) updates.Add(u);

        Assert.Equal("fast", Assert.Single(updates).Text);
    }

    [Fact]
    public async Task LowConfidenceUsesFallback()
    {
        var deep = new FakeChat("deep");
        var router = new DecisionRoutingChatClient(new FakeDecisions("fast", .55),
            new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast"), ["deep"] = deep }, Options(.8, "deep"));

        await router.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(1, deep.Calls);
    }

    [Fact]
    public async Task LowConfidenceWithoutFallbackThrows()
    {
        var router = new DecisionRoutingChatClient(new FakeDecisions("fast", .55),
            new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast"), ["deep"] = new FakeChat("deep") }, Options(.8));

        await Assert.ThrowsAsync<DecisionException>(() => router.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task RequiresUserText()
    {
        var router = new DecisionRoutingChatClient(new FakeDecisions("fast"),
            new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast"), ["deep"] = new FakeChat("deep") }, Options());

        await Assert.ThrowsAsync<ArgumentException>(() => router.GetResponseAsync([new ChatMessage(ChatRole.Assistant, "hi")]));
    }

    [Fact]
    public void ValidatesConstruction()
    {
        var one = new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast") };
        Assert.Throws<ArgumentException>(() => new DecisionRoutingChatClient(new FakeDecisions("fast"), one, Options()));
        var two = new Dictionary<string, IChatClient> { ["fast"] = new FakeChat("fast"), ["deep"] = new FakeChat("deep") };
        Assert.Throws<ArgumentException>(() => new DecisionRoutingChatClient(new FakeDecisions("fast"), two, Options(fallback: "missing")));
    }
}
