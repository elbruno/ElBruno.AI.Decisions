using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Ollama.Tests;

public sealed class OllamaDecisionClientTests
{
    private sealed class Handler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(Body);
        }
    }

    private static string Reply(string answer) => $$$"""
        {"model":"nimble","answers":{"decision":{{{answer}}}},"usage":{"input_tokens":174,"output_tokens":1}}
        """;
    private const string Choice = """{"type":"choice","choice":"billing","probabilities":{"billing":0.75,"support":0.25},"confidence":0.1887}""";
    private static readonly Dictionary<string, string?> Teams = new() { ["billing"] = "Payments", ["support"] = null };

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OllamaDecisionClient Client, Handler Handler) Create(Func<string, HttpResponseMessage> respond, Uri? endpoint = null)
    {
        var handler = new Handler(respond);
        return (new OllamaDecisionClient(new HttpClient(handler), new OllamaDecisionOptions
        {
            Model = "nimble",
            Endpoint = endpoint ?? new Uri("http://localhost:11434/")
        }), handler);
    }

    [Fact]
    public async Task ChooseUsesNativeSystemOneAndPreservesConfidence()
    {
        var (client, handler) = Create(_ => Json(Reply(Choice)));
        ChoiceDecision result = await client.ChooseAsync("refund", "Which team?", Teams);

        Assert.Equal("billing", result.Choice);
        Assert.Equal(.75, result.Probabilities["billing"]);
        Assert.Equal(.1887, result.Confidence);
        Assert.Equal("http://localhost:11434/v1/systemone", handler.Request!.RequestUri!.ToString());
        Assert.Empty(handler.Request.Headers);
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("nimble", (string?)body["model"]);
        Assert.Equal("refund", (string?)body["state"]);
        Assert.Equal("choice", (string?)body["questions"]!["decision"]!["type"]);
        Assert.Equal("Payments", (string?)body["questions"]!["decision"]!["criteria"]!["billing"]);
        Assert.Null(body["questions"]!["decision"]!["criteria"]!["support"]);
        Assert.Equal(3, body.AsObject().Count);
    }

    [Fact]
    public async Task ExplicitEndpointIsPreserved()
    {
        var endpoint = new Uri("http://localhost:11434/v1/systemone");
        var (client, handler) = Create(_ => Json(Reply(Choice)), endpoint);
        await client.ChooseAsync("t", "q", Teams);
        Assert.Equal(endpoint, handler.Request!.RequestUri);
    }

    [Fact]
    public async Task ScoreReturnsOrderedLevelDistribution()
    {
        var (client, handler) = Create(_ => Json(Reply(
            """{"type":"score","score":1.25,"legend":{"0":"bad","1":"ok","2":"good"},"probabilities":{"2":0.5,"0":0.25,"1":0.25},"confidence":0.0536}""")));
        ScoreDecision result = await client.ScoreAsync("t", "q", ["bad", "ok", "good"]);
        Assert.Equal([.25, .25, .5], result.LevelProbabilities);
        Assert.Equal(1.25, result.Score);
        JsonNode question = JsonNode.Parse(handler.Body!)!["questions"]!["decision"]!;
        Assert.Equal("score", (string?)question["type"]);
        Assert.Equal(new[] { "bad", "ok", "good" }, question["criteria"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task AssessUsesNativeNoulProbability()
    {
        var (client, handler) = Create(_ => Json(Reply("""{"type":"noul","noul":0.9}""")));
        AssessmentDecision result = await client.AssessAsync("t", "it is safe");
        Assert.Equal(.9, result.Probability);
        JsonNode question = JsonNode.Parse(handler.Body!)!["questions"]!["decision"]!;
        Assert.Equal("noul", (string?)question["type"]);
        Assert.Equal("it is safe", (string?)question["instructions"]);
        Assert.Null(question["criteria"]);
    }

    [Theory]
    [InlineData("""{"message":{"content":"A"},"logprobs":[]}""")]
    [InlineData("""{"answers":{"other":{"type":"choice"}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"noul","noul":0.9}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"other","probabilities":{"billing":0.75,"support":0.25},"confidence":0.5}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"billing","probabilities":{"billing":0.2,"support":0.8},"confidence":0.5}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"billing","probabilities":{"billing":0.5},"confidence":0.5}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"billing","probabilities":{"billing":0.6,"support":0.6},"confidence":0.5}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"billing","probabilities":{"billing":0.75,"support":0.25},"confidence":1.1}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"billing","probabilities":{"billing":0.75,"billing":0.75,"support":0.25},"confidence":0.5}}}""")]
    [InlineData("not json")]
    public async Task InvalidAnswersAreRejected(string response)
    {
        var (client, _) = Create(_ => Json(response));
        await Assert.ThrowsAsync<DecisionException>(() => client.ChooseAsync("t", "q", Teams));
    }

    [Theory]
    [InlineData(0.5, "bad")]
    [InlineData(1.25, "wrong")]
    public async Task InconsistentScoreOrLegendIsRejected(double score, string firstLevel)
    {
        var answer = new JsonObject
        {
            ["type"] = "score",
            ["score"] = score,
            ["confidence"] = .2,
            ["legend"] = new JsonObject { ["0"] = firstLevel, ["1"] = "ok", ["2"] = "good" },
            ["probabilities"] = new JsonObject { ["0"] = .25, ["1"] = .25, ["2"] = .5 }
        };
        var (client, _) = Create(_ => Json(Reply(answer.ToJsonString())));
        await Assert.ThrowsAsync<DecisionException>(() => client.ScoreAsync("t", "q", ["bad", "ok", "good"]));
    }

    [Theory]
    [InlineData("""{"type":"score","score":"1.25","legend":{"0":"bad","1":"ok","2":"good"},"probabilities":{"0":0.25,"1":0.25,"2":0.5},"confidence":0.2}""")]
    [InlineData("""{"type":"score","score":1.25,"legend":{"0":"bad","0":"bad","2":"good"},"probabilities":{"0":0.25,"1":0.25,"2":0.5},"confidence":0.2}""")]
    [InlineData("""{"type":"score","score":1.25,"legend":{"0":"bad","1":"ok","2":"good"},"probabilities":{"0":0.25,"1":0.25,"2":0.5},"confidence":null}""")]
    public async Task MalformedScoreAnswersAreRejected(string answer)
    {
        var (client, _) = Create(_ => Json(Reply(answer)));
        await Assert.ThrowsAsync<DecisionException>(() => client.ScoreAsync("t", "q", ["bad", "ok", "good"]));
    }

    [Fact]
    public async Task RoundedProbabilitiesAreAcceptedWithoutChangingConfidence()
    {
        var (client, _) = Create(_ => Json(Reply(
            """{"type":"choice","choice":"billing","probabilities":{"billing":0.7499,"support":0.25},"confidence":0.1887}""")));
        ChoiceDecision result = await client.ChooseAsync("t", "q", Teams);
        Assert.Equal(.7499, result.Probabilities["billing"]);
        Assert.Equal(.1887, result.Confidence);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpErrorsBecomeDecisionException(HttpStatusCode status)
    {
        var (client, _) = Create(_ => Json("""{"error":"private input"}""", status));
        DecisionException ex = await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
        Assert.Equal((int)status, ex.StatusCode);
        Assert.DoesNotContain("private input", ex.Message);
    }

    [Fact]
    public async Task OversizedErrorBodyDoesNotHideHttpStatus()
    {
        var (client, _) = Create(_ => Json(new string('x', (4 << 20) + 1), HttpStatusCode.RequestEntityTooLarge));
        DecisionException ex = await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
        Assert.Equal(413, ex.StatusCode);
    }

    [Fact]
    public async Task OversizedUtf8RequestIsRejectedBeforeTransport()
    {
        var (client, handler) = Create(_ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<ArgumentException>(() => client.AssessAsync(new string('x', 65536), "p"));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ResponseSizeIsBounded()
    {
        var (client, _) = Create(_ => Json(new string('x', (4 << 20) + 1)));
        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
    }

    [Fact]
    public async Task ChoicesAreNotLimitedToLetterMapping()
    {
        Dictionary<string, string?> options = Enumerable.Range(0, 27).ToDictionary(i => $"o{i}", _ => (string?)null);
        var answer = new JsonObject
        {
            ["type"] = "choice",
            ["choice"] = "o0",
            ["confidence"] = 1,
            ["probabilities"] = new JsonObject(options.Select((o, i) => new KeyValuePair<string, JsonNode?>(o.Key, JsonValue.Create(i == 0 ? 1d : 0d))))
        };
        var (client, _) = Create(_ => Json(Reply(answer.ToJsonString())));
        Assert.Equal("o0", (await client.ChooseAsync("t", "q", options)).Choice);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        var (client, _) = Create(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AssessAsync("t", "p", cts.Token));
    }

    [Fact]
    public async Task TransportTimeoutIsReported()
    {
        var (client, _) = Create(_ => throw new OperationCanceledException());
        DecisionException ex = await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public void ModelIsRequired() =>
        Assert.Throws<ArgumentException>(() => new OllamaDecisionClient(new OllamaDecisionOptions()));
}
