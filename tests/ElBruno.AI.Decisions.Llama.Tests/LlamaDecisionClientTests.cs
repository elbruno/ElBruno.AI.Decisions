using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.AI.Decisions.Llama;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.AI.Decisions.Llama.Tests;

public sealed class LlamaDecisionClientTests
{
    private const string Response = """
        {"model":"omni","answers":{"flag":{"type":"noul","noul":0.8},"team":{"type":"choice","choice":"a","probabilities":{"a":0.75,"b":0.25},"confidence":0.45},"priority":{"type":"score","score":1,"probabilities":{"0":0.3,"1":0.7},"confidence":0.4,"legend":{"0":"low","1":"high"}}},"usage":{"input_tokens":42,"output_tokens":0}}
        """;
    private static LlamaDecisionRequest Batch() => new("state", new Dictionary<string, LlamaQuestion>
    {
        ["team"] = new LlamaChoiceQuestion("team?", new Dictionary<string, string?> { ["a"] = "A", ["b"] = "B" }),
        ["priority"] = new LlamaScoreQuestion("priority?", ["low", "high"]),
        ["flag"] = new LlamaNoulQuestion("flag?")
    });

    [Fact]
    public async Task SerializesAndCorrelatesMultimodalBatch()
    {
        var handler = new Handler(_ => Response);
        using var http = new HttpClient(handler);
        using var client = new LlamaDecisionClient(http, new() { Model = "omni" });
        LlamaDecisionRequest batch = Batch() with
        {
            Images = [new("image/png", [1, 2])],
            Audio = [new("audio/wav", [3, 4])]
        };
        LlamaDecisionResponse result = await client.DecideAsync(batch);
        Assert.Equal("http://localhost:9931/v1/systemone", handler.Endpoint);
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("omni", (string?)body["model"]);
        Assert.Equal("data:image/png;base64,AQI=", (string?)body["images"]![0]);
        Assert.Equal("data:audio/wav;base64,AwQ=", (string?)body["files"]![0]);
        Assert.Equal("A", (string?)body["questions"]!["team"]!["criteria"]!["a"]);
        Assert.Equal("omni", result.Model);
        Assert.Equal(42, result.Usage!.InputTokens);
        Assert.Equal(.45, Assert.IsType<LlamaChoiceAnswer>(result.Answers["team"]).Confidence);
        Assert.Equal(1, Assert.IsType<LlamaScoreAnswer>(result.Answers["priority"]).Score);
        Assert.Equal(.8, Assert.IsType<LlamaNoulAnswer>(result.Answers["flag"]).Probability);
    }

    [Theory]
    [InlineData("""{"answers":{}}""")]
    [InlineData("""{"answers":{"flag":{"type":"noul","noul":0.2},"flag":{"type":"noul","noul":0.3}}}""")]
    [InlineData("""{"answers":{"other":{"type":"noul","noul":0.2}}}""")]
    [InlineData("""{"answers":{"flag":{"type":"noul","noul":1.1}}}""")]
    [InlineData("not json")]
    public async Task RejectsInvalidResponses(string body)
    {
        using var http = new HttpClient(new Handler(_ => body));
        using var client = new LlamaDecisionClient(http, new());
        await Assert.ThrowsAsync<DecisionException>(() => client.DecideAsync(Batch()));
    }

    [Fact]
    public async Task SharedClientPreservesUsageAndDoesNotOwnInjectedHttp()
    {
        using var http = new HttpClient(new Handler(_ => """{"answers":{"decision":{"type":"noul","noul":0.8}},"usage":{"input_tokens":2}}"""));
        var client = new LlamaDecisionClient(http, new());
        Assert.Equal(2, (await client.AssessAsync("state", "question")).Usage!.InputTokens);
        client.Dispose();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("http://localhost/")).StatusCode);
    }

    [Fact]
    public async Task ValidatesMediaAndProtectsHttpErrors()
    {
        using var http = new HttpClient(new Handler(_ => "private media", HttpStatusCode.BadRequest));
        using var client = new LlamaDecisionClient(http, new());
        await Assert.ThrowsAsync<ArgumentException>(() => client.DecideAsync(Batch() with { Images = [new("image/gif", [1])] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.DecideAsync(Batch() with { Audio = [new("audio/wav", new byte[8 * 1024 * 1024 + 1])] }));
        DecisionException error = await Assert.ThrowsAsync<DecisionException>(() => client.DecideAsync(Batch()));
        Assert.Equal(400, error.StatusCode);
        Assert.DoesNotContain("private media", error.ToString());
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var http = new HttpClient(new WaitingHandler());
        using var client = new LlamaDecisionClient(http, new());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DecideAsync(Batch(), cancellation.Token));
    }

    [Fact]
    public async Task TimeoutIsReportedWithoutDisposingCallerHttpClient()
    {
        using var http = new HttpClient(new WaitingHandler());
        using var client = new LlamaDecisionClient(http, new() { Timeout = TimeSpan.FromMilliseconds(20) });
        DecisionException error = await Assert.ThrowsAsync<DecisionException>(() => client.DecideAsync(Batch()));
        Assert.Contains("timed out", error.Message);
        Assert.NotNull(http.DefaultRequestHeaders);
    }

    [Fact]
    public async Task RejectsInvalidChoiceDistributionAndWrongType()
    {
        foreach (string answer in new[]
        {
            """{"type":"choice","choice":"a","probabilities":{"a":0.2,"b":0.8}}""",
            """{"type":"choice","choice":"a","probabilities":{"a":0.9}}""",
            """{"type":"noul","noul":0.9}"""
        })
        {
            using var http = new HttpClient(new Handler(_ => $$$"""{"answers":{"decision":{{{answer}}}}}"""));
            using var client = new LlamaDecisionClient(http, new());
            await Assert.ThrowsAsync<DecisionException>(() => client.ChooseAsync("text", "question", new Dictionary<string, string?> { ["a"] = null, ["b"] = null }));
        }
    }

    [Fact]
    public async Task SharedScoreUsesDistributionWhileNativeScoreIsPreserved()
    {
        using var http = new HttpClient(new Handler(_ => """{"answers":{"decision":{"type":"score","score":1,"probabilities":{"0":0.3,"1":0.7},"confidence":0.4}},"usage":{"input_tokens":3}}"""));
        using var client = new LlamaDecisionClient(http, new());
        ScoreDecision score = await client.ScoreAsync("text", "question", ["low", "high"]);
        Assert.Equal(.7, score.Score, 6);
        Assert.Equal(3, score.Usage!.InputTokens);
    }

    [Fact]
    public void DependencyInjectionExposesSameClientForBothApis()
    {
        var services = new ServiceCollection();
        services.AddLlamaDecisions(options => options.Model = "omni");
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<LlamaDecisionClient>(), provider.GetRequiredService<IDecisionClient>());
    }

    [Fact]
    public async Task RejectsOversizedResponse()
    {
        using var http = new HttpClient(new Handler(_ => new string('x', 4 * 1024 * 1024 + 1)));
        using var client = new LlamaDecisionClient(http, new());
        DecisionException error = await Assert.ThrowsAsync<DecisionException>(() => client.DecideAsync(Batch()));
        Assert.Contains("too large", error.Message);
    }

    private sealed class Handler(Func<string, string> reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Endpoint { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Endpoint = request.RequestUri!.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(reply(Body), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
