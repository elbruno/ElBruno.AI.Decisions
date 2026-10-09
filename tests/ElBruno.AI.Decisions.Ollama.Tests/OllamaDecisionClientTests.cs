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

    private static string Reply(params (string Token, double LogProb)[] top) =>
        new JsonObject
        {
            ["message"] = new JsonObject { ["content"] = top[0].Token },
            ["logprobs"] = new JsonArray(new JsonObject
            {
                ["token"] = top[0].Token,
                ["top_logprobs"] = new JsonArray([.. top.Select(t => (JsonNode)new JsonObject { ["token"] = t.Token, ["logprob"] = t.LogProb })])
            })
        }.ToJsonString();

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OllamaDecisionClient Client, Handler Handler) Create(Func<string, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        return (new OllamaDecisionClient(new HttpClient(handler), new OllamaDecisionOptions { Model = "llama3.2" }), handler);
    }

    [Fact]
    public async Task ChooseNormalizesLetterLogProbabilities()
    {
        var (client, handler) = Create(_ => Json(Reply(("A", Math.Log(.6)), ("B", Math.Log(.2)), ("Neither", Math.Log(.2)))));

        ChoiceDecision result = await client.ChooseAsync(
            "refund", "Which team?", new Dictionary<string, string?> { ["billing"] = "Payments", ["support"] = null });

        Assert.Equal("billing", result.Choice);
        Assert.Equal(.75, result.Probabilities["billing"], 6);
        Assert.Equal(.25, result.Probabilities["support"], 6);
        Assert.Equal("http://localhost:11434/api/chat", handler.Request!.RequestUri!.ToString());
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("llama3.2", (string?)body["model"]);
        Assert.True((bool)body["logprobs"]!);
        Assert.Contains("A: billing: Payments", (string)body["messages"]![1]!["content"]!);
    }

    [Fact]
    public async Task ScoreReturnsLevelDistribution()
    {
        var (client, _) = Create(_ => Json(Reply(("C", Math.Log(.5)), (" B", Math.Log(.25)), ("a", Math.Log(.25)))));

        ScoreDecision result = await client.ScoreAsync("t", "q", ["bad", "ok", "good"]);

        Assert.Equal([.25, .25, .5], result.LevelProbabilities.Select(p => Math.Round(p, 6)));
        Assert.Equal(1.25, result.Score, 6);
    }

    [Fact]
    public async Task AssessUsesYesAsFirstOption()
    {
        var (client, _) = Create(_ => Json(Reply(("A", Math.Log(.9)), ("B", Math.Log(.1)))));

        AssessmentDecision result = await client.AssessAsync("t", "it is safe");

        Assert.Equal(.9, result.Probability, 6);
    }

    [Fact]
    public async Task MissingLogProbabilitiesAreReported()
    {
        var (client, _) = Create(_ => Json("""{"message":{"content":"A"}}"""));

        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
    }

    [Fact]
    public async Task NonLetterAnswerIsRejected()
    {
        var (client, _) = Create(_ => Json(Reply(("Sure", -0.1), ("Yes", -2))));

        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
    }

    [Fact]
    public async Task HttpErrorsBecomeDecisionException()
    {
        var (client, _) = Create(_ => Json("""{"error":"model not found"}""", HttpStatusCode.NotFound));

        DecisionException ex = await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task TooManyOptionsAreRejected()
    {
        var (client, _) = Create(_ => throw new InvalidOperationException());
        Dictionary<string, string?> options = Enumerable.Range(0, 27).ToDictionary(i => $"o{i}", _ => (string?)null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ChooseAsync("t", "q", options));
    }

    [Fact]
    public void ModelIsRequired() =>
        Assert.Throws<ArgumentException>(() => new OllamaDecisionClient(new OllamaDecisionOptions()));
}
