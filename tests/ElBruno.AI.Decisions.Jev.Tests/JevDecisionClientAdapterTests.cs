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
        new("m", new Dictionary<string, JevAnswer> { ["decision"] = answer });

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
    }

    [Fact]
    public async Task AssessMapsNoulProbability()
    {
        IDecisionClient client = new JevDecisionClientAdapter(new Stub(_ => Response(new JevNoulAnswer(.42))));

        AssessmentDecision result = await client.AssessAsync("text", "is true");

        Assert.Equal(.42, result.Probability);
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
