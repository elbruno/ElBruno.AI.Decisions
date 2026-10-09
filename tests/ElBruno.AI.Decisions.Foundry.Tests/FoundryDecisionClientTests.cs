using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry.Tests;

public sealed class FoundryDecisionClientTests
{
    private const string PredicateReply = """{"answers":{"decision":{"type":"noul","noul":1}}}""";
    private sealed class Credential : Azure.Core.TokenCredential
    {
        public int Calls { get; private set; }
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Assert.Equal(["https://cognitiveservices.azure.com/.default"], requestContext.Scopes);
            Calls++;
            return ValueTask.FromResult(new Azure.Core.AccessToken("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutApiKeyUsesTokenCredential(string? key)
    {
        var credential = new Credential();
        var handler = new Handler((_, _) => Json(PredicateReply));
        FoundryDecisionOptions options = Options();
        options.ApiKey = key;
        options.Credential = credential;
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        await client.AssessAsync("t", "p");
        Assert.Equal(1, credential.Calls);
        Assert.Equal("synthetic-token", handler.Request!.Headers.Authorization!.Parameter);
        Assert.False(handler.Request.Headers.Contains("api-key"));
    }

    [Fact]
    public async Task ApiKeyTakesPrecedenceOverCredential()
    {
        var credential = new Credential();
        var handler = new Handler((_, _) => Json(PredicateReply));
        FoundryDecisionOptions options = Options();
        options.Credential = credential;
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        await client.AssessAsync("t", "p");
        Assert.Equal(0, credential.Calls);
        Assert.Equal("test-key", handler.Request!.Headers.GetValues("api-key").Single());
    }
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, Body ?? "");
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FoundryDecisionOptions Options() => new()
    {
        Endpoint = new Uri("https://example.services.ai.azure.com/score"),
        ApiKey = "test-key"
    };

    private static (FoundryDecisionClient Client, Handler Handler) Create(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        return (new FoundryDecisionClient(new HttpClient(handler), Options()), handler);
    }

    [Theory]
    [InlineData("https://example.com/", "https://example.com/providers/microsoft/v1/systemone")]
    [InlineData("https://example.com/custom?api-version=preview", "https://example.com/custom?api-version=preview")]
    public async Task ResolvesResourceRootButPreservesExplicitUrl(string endpoint, string expected)
    {
        var handler = new Handler((_, _) => Json(PredicateReply));
        var options = Options();
        options.Endpoint = new Uri(endpoint);
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        await client.AssessAsync("text", "proposition");
        Assert.Equal(expected, handler.Request!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("""{"a":0.5,"a":0.5}""")]
    [InlineData("""{"a":0.5}""")]
    [InlineData("""{"a":0.5,"c":0.5}""")]
    [InlineData("""{"a":0.2,"b":0.2}""")]
    public async Task InvalidChoiceDistributionsAreRejected(string probabilities)
    {
        var (client, _) = Create((_, _) => Json(
            """{"answers":{"decision":{"type":"choice","choice":"a","confidence":0.5,"probabilities":""" + probabilities + "}}}"));
        await Assert.ThrowsAsync<DecisionException>(() => client.ChooseAsync("t", "q",
            new Dictionary<string, string?> { ["a"] = null, ["b"] = null }));
    }

    [Fact]
    public async Task ChooseSendsOptionsAndPicksHighestProbability()
    {
        var (client, handler) = Create((_, _) => Json("""{"answers":{"decision":{"type":"choice","choice":"billing","confidence":0.7,"probabilities":{"billing":0.8,"support":0.2}}}}"""));

        ChoiceDecision result = await client.ChooseAsync(
            "refund my order", "Which team?", new Dictionary<string, string?> { ["billing"] = "Payments", ["support"] = null });

        Assert.Equal("billing", result.Choice);
        Assert.Equal(.2, result.Probabilities["support"]);
        Assert.Equal(.7, result.Confidence);
        Assert.Equal("test-key", handler.Request!.Headers.GetValues("api-key").Single());
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("refund my order", (string?)body["state"]);
        Assert.Equal("Which team?", (string?)body["questions"]!["decision"]!["instructions"]);
        Assert.Equal("choice", (string?)body["questions"]!["decision"]!["type"]);
        Assert.Equal(2, body["questions"]!["decision"]!["criteria"]!.AsObject().Count);
    }

    [Fact]
    public async Task ParsesArrayOfLabelAndProbability()
    {
        var (client, _) = Create((_, _) => Json("""{"answers":{"decision":{"type":"choice","choice":"b","confidence":0.4,"probabilities":{"a":0.6,"b":0.4}}}}"""));

        ChoiceDecision result = await client.ChooseAsync("t", "q", new Dictionary<string, string?> { ["a"] = null, ["b"] = null });

        Assert.Equal("b", result.Choice);
    }

    [Fact]
    public async Task ScoreMapsLevelsByIndex()
    {
        var (client, handler) = Create((_, _) => Json("""{"answers":{"decision":{"type":"score","score":1.6,"confidence":0.5,"probabilities":{"2":0.7,"0":0.1,"1":0.2}}}}"""));

        ScoreDecision result = await client.ScoreAsync("t", "q", ["bad", "ok", "good"]);

        Assert.Equal([.1, .2, .7], result.LevelProbabilities);
        Assert.Equal(1.6, result.Score, 6);
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("score", (string?)body["questions"]!["decision"]!["type"]);
        Assert.Equal("bad", (string?)body["questions"]!["decision"]!["criteria"]![0]);
    }

    [Fact]
    public async Task AssessReturnsYesProbability()
    {
        var (client, handler) = Create((_, _) => Json("""{"answers":{"decision":{"type":"noul","noul":0.93}}}"""));

        AssessmentDecision result = await client.AssessAsync("t", "it is safe");

        Assert.Equal(.93, result.Probability);
        Assert.Equal("noul", (string?)JsonNode.Parse(handler.Body!)!["questions"]!["decision"]!["type"]);
    }

    [Fact]
    public async Task BearerHeaderIsUsedWhenConfigured()
    {
        var handler = new Handler((_, _) => Json(PredicateReply));
        FoundryDecisionOptions options = Options();
        options.ApiKeyHeaderName = "Authorization";
        var client = new FoundryDecisionClient(new HttpClient(handler), options);

        await client.AssessAsync("t", "p");

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task HttpErrorsBecomeDecisionExceptionWithoutLeakingKey()
    {
        var (client, _) = Create((_, _) => Json("""{"error":"test-key rejected"}""", HttpStatusCode.Unauthorized));

        DecisionException ex = await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));

        Assert.Equal(401, ex.StatusCode);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"probabilities":{"yes":0.5}}""")]
    [InlineData("""{"probabilities":{"yes":2,"no":0}}""")]
    [InlineData("""{"answers":{"decision":{"type":"refusal"}}}""")]
    [InlineData("""{"answers":{"other":{"type":"noul","noul":0.5}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"noul","noul":2}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice"}}}""")]
    public async Task MalformedResponsesAreRejected(string body)
    {
        var (client, _) = Create((_, _) => Json(body));

        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        var (client, _) = Create((_, _) => Json(PredicateReply));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AssessAsync("t", "p", cts.Token));
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("http://insecure.example/score", "key")]
    public void InvalidOptionsAreRejected(string? endpoint, string key)
    {
        var options = new FoundryDecisionOptions { Endpoint = endpoint is null ? null : new Uri(endpoint), ApiKey = key };

        Assert.Throws<ArgumentException>(() => new FoundryDecisionClient(options));
    }
}
