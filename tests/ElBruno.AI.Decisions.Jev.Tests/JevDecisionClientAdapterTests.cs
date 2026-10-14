namespace ElBruno.AI.Decisions.Jev.Tests;

public sealed class JevDecisionClientAdapterTests
{
    private sealed class Stub(Func<JevDecisionRequest, JevDecisionResponse> respond) : IJevDecisionClient
    {
        public JevDecisionRequest? Request { get; private set; }

        public Task<JevDecisionResponse> EvaluateAsync(JevDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }

        public Task<JevModelList> ListModelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static JevDecisionResponse Response(JevAnswer answer) =>
        new("m", new Dictionary<string, JevAnswer> { ["decision"] = answer },
            new JevUsage(12, 3) { TotalTokens = 20, ReasoningTokens = 2, CachedTokens = 0 });

    [Fact]
    public async Task ChooseMapsChoiceAnswer()
    {
        var stub = new Stub(_ => Response(new JevChoiceAnswer("a", new Dictionary<string, double> { ["a"] = .7, ["b"] = .3 }, .8)));
        IDecisionClient client = new JevDecisionClientAdapter(stub);

        ChoiceDecision result = await client.ChooseAsync("text", "pick", new Dictionary<string, string?> { ["a"] = null, ["b"] = "second" });

        Assert.Equal("a", result.Choice);
        Assert.Equal(.8, result.Confidence);
        Assert.Equal(.3, result.Probabilities["b"]);
        Assert.Contains("decision", stub.Request!.Questions.Keys);
        Assert.Equal(12, result.Usage!.InputTokens);
        Assert.Equal(3, result.Usage.OutputTokens);
        Assert.Equal(20, result.Usage.TotalTokens);
        Assert.Equal(2, result.Usage.ReasoningTokens);
        Assert.Equal(0, result.Usage.CachedTokens);
    }

    [Fact]
    public async Task ScoreMapsLevelsByIndex()
    {
        var stub = new Stub(_ => Response(new JevScoreAnswer(
            .3, new Dictionary<string, double> { ["0"] = .7, ["1"] = .3 }, new Dictionary<string, System.Text.Json.JsonElement>(), .7)));
        IDecisionClient client = new JevDecisionClientAdapter(stub);

        ScoreDecision result = await client.ScoreAsync("text", "grade", ["bad", "good"]);

        Assert.Equal([.7, .3], result.LevelProbabilities);
        Assert.Equal(.3, result.Score, 6);
        Assert.Equal(12, result.Usage!.InputTokens);
    }

    [Fact]
    public async Task AssessMapsNoulProbability()
    {
        IDecisionClient client = new JevDecisionClientAdapter(new Stub(_ => Response(new JevNoulAnswer(.42))));

        AssessmentDecision result = await client.AssessAsync("text", "is true");

        Assert.Equal(.42, result.Probability);
        Assert.Equal(3, result.Usage!.OutputTokens);
    }

    [Fact]
    public async Task MissingNativeUsageRemainsNullOnTypedResult()
    {
        var stub = new Stub(_ => new JevDecisionResponse("m", new Dictionary<string, JevAnswer> { ["decision"] = new JevNoulAnswer(.8) }));
        var client = new JevDecisionClientAdapter(stub);
        Assert.Null((await client.AssessAsync("t", "p")).Usage);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejectedBeforeCallingJev()
    {
        var stub = new Stub(_ => throw new InvalidOperationException());
        IDecisionClient client = new JevDecisionClientAdapter(stub);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.ChooseAsync("t", "i", new Dictionary<string, string?> { ["only"] = null }));
        Assert.Null(stub.Request);
    }
}
