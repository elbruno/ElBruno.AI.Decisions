using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry.Tests;

public sealed class FoundryDecisionClientTests
{
    private const string PredicateReply = """{"answers":{"decision":{"type":"noul","noul":1}}}""";
    private const string OpenAIPredicateReply = """{"answers":[{"type":"predicate","name":"decision","probability":0.93}]}""";
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
    public async Task OpenAIDecisionsUsesItsEndpointAndPredicateContract()
    {
        var handler = new Handler((_, _) => Json(OpenAIPredicateReply));
        FoundryDecisionOptions options = Options();
        options.Endpoint = new Uri("https://example.openai.azure.com");
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        options.Model = "gpt-6-luna-deployment";
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);

        AssessmentDecision result = await client.AssessAsync("text", "the text is safe");

        Assert.Equal(.93, result.Probability);
        Assert.Equal("https://example.openai.azure.com/openai/v1/decisions", handler.Request!.RequestUri!.ToString());
        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("gpt-6-luna-deployment", (string?)body["model"]);
        Assert.Equal("text", (string?)body["input"]);
        Assert.Equal("predicate", (string?)body["questions"]![0]!["type"]);
        Assert.Equal("decision", (string?)body["questions"]![0]!["name"]);
        Assert.Equal("the text is safe", (string?)body["questions"]![0]!["instructions"]);
    }

    [Theory]
    [InlineData("https://example.com/", "https://example.com/openai/v1/decisions")]
    [InlineData("https://example.com/openai/v1/", "https://example.com/openai/v1/decisions")]
    [InlineData("https://example.com/openai/v1/?api-version=preview", "https://example.com/openai/v1/decisions?api-version=preview")]
    [InlineData("https://example.com/?api-version=preview", "https://example.com/openai/v1/decisions?api-version=preview")]
    [InlineData("https://example.com/openai/v1?api-version=preview", "https://example.com/openai/v1/decisions?api-version=preview")]
    [InlineData("https://example.com/openai/v1/decisions?api-version=preview", "https://example.com/openai/v1/decisions?api-version=preview")]
    [InlineData("https://example.com/custom?api-version=preview", "https://example.com/custom?api-version=preview")]
    public async Task OpenAIDecisionsResolvesResourceRootsAndPreservesFullUrls(string endpoint, string expected)
    {
        var handler = new Handler((_, _) => Json(OpenAIPredicateReply));
        FoundryDecisionOptions options = Options();
        options.Endpoint = new Uri(endpoint);
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);

        await client.AssessAsync("text", "safe");

        Assert.Equal(expected, handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task OpenAIDecisionsMapsChoiceAndScorePayloadsAndAnswers()
    {
        var choiceHandler = new Handler((_, _) => Json(
            """{"answers":[{"type":"choice","name":"decision","choice":"billing","probabilities":[{"value":"billing","probability":0.8},{"value":"support","probability":0.2}],"confidence":0.7}]}"""));
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        using var choiceHttp = new HttpClient(choiceHandler);
        using var choiceClient = new FoundryDecisionClient(choiceHttp, options);

        ChoiceDecision choice = await choiceClient.ChooseAsync("text", "choose", new Dictionary<string, string?>
        {
            ["billing"] = "Payments",
            ["support"] = null
        });

        Assert.Equal("billing", choice.Choice);
        Assert.Equal(.8, choice.Probabilities["billing"]);
        Assert.Equal(.7, choice.Confidence);
        JsonNode choiceBody = JsonNode.Parse(choiceHandler.Body!)!;
        Assert.Equal("Payments", (string?)choiceBody["questions"]![0]!["choices"]![0]!["description"]);
        Assert.Null(choiceBody["questions"]![0]!["choices"]![1]!["description"]);

        var scoreHandler = new Handler((_, _) => Json(
            """{"answers":[{"type":"score","name":"decision","score":1.6,"probabilities":[{"value":0,"label":"0","probability":0.1},{"value":1,"label":"1","probability":0.2},{"value":2,"label":"2","probability":0.7}],"confidence":0.55}]}"""));
        using var scoreHttp = new HttpClient(scoreHandler);
        using var scoreClient = new FoundryDecisionClient(scoreHttp, options);

        ScoreDecision score = await scoreClient.ScoreAsync("text", "rate", ["bad", "ok", "good"]);

        Assert.Equal(1.6, score.Score, 6);
        Assert.Equal([.1, .2, .7], score.LevelProbabilities);
        Assert.Equal("bad", (string?)JsonNode.Parse(scoreHandler.Body!)!["questions"]![0]!["levels"]![0]!["description"]);
    }

    [Fact]
    public async Task DecideSupportsMixedQuestionsImagesUsageAndNameCorrelation()
    {
        const string response = """
            {
              "model":"gpt-6-luna",
              "answers":[
                {"type":"score","name":"severity","score":1.25,"probabilities":[
                  {"value":0,"label":"low","probability":0.25},
                  {"value":1,"label":"high","probability":0.25},
                  {"value":2,"label":"critical","probability":0.5}],"confidence":0.6},
                {"type":"predicate","name":"has_damage","probability":0.9},
                {"type":"choice","name":"category","choice":"electronics","probabilities":[
                  {"value":"electronics","probability":0.8},{"value":"furniture","probability":0.2}],"confidence":0.75}
              ],
              "usage":{"input_tokens":100,"output_tokens":12,"total_tokens":112}
            }
            """;
        var handler = new Handler((_, _) => Json(response));
        FoundryDecisionOptions options = Options();
        options.Endpoint = new Uri("https://example.openai.azure.com/openai/v1/");
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        options.Model = "gpt-6-luna";
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        FoundryDecisionInput input = FoundryDecisionInput.FromMessages([
            FoundryDecisionMessage.User([
                FoundryDecisionContentPart.Text("Inspect the attached item."),
                FoundryDecisionContentPart.Image("https://example.com/item.jpg")
            ])
        ]);
        FoundryDecisionQuestion[] questions =
        [
            new FoundryChoiceQuestion("category", "What category is this?", [
                new FoundryDecisionChoice("electronics", "Electronic products"),
                new FoundryDecisionChoice("furniture", "Furniture")
            ]),
            new FoundryPredicateQuestion("has_damage", "Is there visible damage?"),
            new FoundryScoreQuestion("severity", "How severe is the damage?", [
                new FoundryDecisionLevel("low"),
                new FoundryDecisionLevel("high"),
                new FoundryDecisionLevel("critical")
            ])
        ];

        FoundryDecisionResult result = await client.DecideAsync(input, questions);

        Assert.Equal("gpt-6-luna", result.Model);
        Assert.Equal(["category", "has_damage", "severity"], result.Answers.Select(answer => answer.Name));
        Assert.Equal("electronics", Assert.IsType<FoundryChoiceAnswer>(result.Answers[0]).Choice);
        Assert.Equal(.8, Assert.IsType<FoundryChoiceAnswer>(result.Answers[0]).Probabilities["electronics"]);
        Assert.Equal(.9, Assert.IsType<FoundryPredicateAnswer>(result.Answers[1]).Probability);
        Assert.Equal(1.25, Assert.IsType<FoundryScoreAnswer>(result.Answers[2]).Score);
        Assert.Equal(112, result.Usage!.TotalTokens);

        JsonNode body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("gpt-6-luna", (string?)body["model"]);
        Assert.Equal("Inspect the attached item.", (string?)body["input"]![0]!["content"]![0]!["text"]);
        Assert.Equal("https://example.com/item.jpg", (string?)body["input"]![0]!["content"]![1]!["image_url"]);
        Assert.Equal("choice", (string?)body["questions"]![0]!["type"]);
        Assert.Equal("predicate", (string?)body["questions"]![1]!["type"]);
        Assert.Equal("score", (string?)body["questions"]![2]!["type"]);
    }

    [Fact]
    public async Task DecideKeepsRefusalsSpecificToTheirQuestion()
    {
        var handler = new Handler((_, _) => Json(
            """{"answers":[{"type":"refusal","name":"sensitive"},{"type":"predicate","name":"ordinary","probability":0.8}]}"""));
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);

        FoundryDecisionResult result = await client.DecideAsync(FoundryDecisionInput.FromText("text"), [
            new FoundryPredicateQuestion("ordinary", "Is it ordinary?"),
            new FoundryPredicateQuestion("sensitive", "Is it sensitive?")
        ]);

        Assert.IsType<FoundryPredicateAnswer>(result.Answers[0]);
        Assert.Equal("sensitive", Assert.IsType<FoundryRefusalAnswer>(result.Answers[1]).Name);
    }

    [Theory]
    [InlineData("""{"answers":[{"type":"predicate","name":"unknown","probability":0.5}]}""")]
    [InlineData("""{"answers":[{"type":"predicate","name":"safe","probability":0.5},{"type":"predicate","name":"safe","probability":0.5}]}""")]
    [InlineData("""{"answers":[]}""")]
    [InlineData("""{"answers":[{"type":"score","name":"rating","score":1.4,"probabilities":[{"value":0,"label":"low","probability":0.1},{"value":1,"label":"high","probability":0.2},{"value":2,"label":"critical","probability":0.7}],"confidence":0.5}]}""")]
    public async Task DecideRejectsUnmatchedAnswersAndInvalidScores(string response)
    {
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        var handler = new Handler((_, _) => Json(response));
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        FoundryDecisionQuestion question = response.Contains("\"rating\"", StringComparison.Ordinal)
            ? new FoundryScoreQuestion("rating", "Rate it.", [
                new FoundryDecisionLevel("low"), new FoundryDecisionLevel("high"), new FoundryDecisionLevel("critical")
            ])
            : new FoundryPredicateQuestion("safe", "Is it safe?");

        await Assert.ThrowsAsync<DecisionException>(() =>
            client.DecideAsync(FoundryDecisionInput.FromText("text"), [question]));
    }

    [Fact]
    public async Task DecideValidatesQuestionCountNamesAndApiMode()
    {
        var handler = new Handler((_, _) => Json(OpenAIPredicateReply));
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);
        var duplicateQuestions = new FoundryDecisionQuestion[]
        {
            new FoundryPredicateQuestion("same", "First."),
            new FoundryPredicateQuestion("same", "Second.")
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.DecideAsync(FoundryDecisionInput.FromText("text"), duplicateQuestions));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.DecideAsync(FoundryDecisionInput.FromText("text"), []));
        Assert.Null(handler.Request);

        options.Api = FoundryDecisionApi.SystemOne;
        using var systemOne = new FoundryDecisionClient(http, options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            systemOne.DecideAsync(FoundryDecisionInput.FromText("text"), [
                new FoundryPredicateQuestion("safe", "Is it safe?")
            ]));
    }

    [Fact]
    public async Task DecideRejectsMoreThanFourHundredQuestions()
    {
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        using var http = new HttpClient(new Handler((_, _) => Json(OpenAIPredicateReply)));
        using var client = new FoundryDecisionClient(http, options);
        FoundryDecisionQuestion[] questions = Enumerable.Range(0, 401)
            .Select(index => (FoundryDecisionQuestion)new FoundryPredicateQuestion($"q{index}", "Is it true?"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.DecideAsync(FoundryDecisionInput.FromText("text"), questions));
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("file:///C:/image.png")]
    [InlineData("relative/image.png")]
    public void ImageInputRequiresHttpsOrDataUrl(string imageUrl) =>
        Assert.Throws<ArgumentException>(() => FoundryDecisionContentPart.Image(imageUrl));

    [Fact]
    public void ImageInputAcceptsSupportedDataUrlsAndEnforcesImageCount()
    {
        FoundryDecisionContentPart image = FoundryDecisionContentPart.Image("data:image/png;base64,AA==");
        Assert.Equal("input_image", image.Type);

        FoundryDecisionContentPart[] tooManyImages = Enumerable.Repeat(image, 129).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FoundryDecisionInput.FromMessages([FoundryDecisionMessage.User(tooManyImages)]));
    }

    [Theory]
    [InlineData("""{"answers":[{"type":"refusal","name":"decision"}]}""")]
    [InlineData("""{"answers":[{"type":"predicate","name":"other","probability":0.5}]}""")]
    [InlineData("""{"answers":[{"type":"predicate","name":"decision","probability":1.5}]}""")]
    [InlineData("""{"answers":[{"type":"choice","name":"decision","choice":"billing","probabilities":[{"value":"billing","probability":0.5},{"value":"billing","probability":0.5}],"confidence":0.5}]}""")]
    public async Task OpenAIDecisionsRejectsRefusalsAndInvalidAnswers(string response)
    {
        FoundryDecisionOptions options = Options();
        options.Api = FoundryDecisionApi.OpenAIDecisions;
        var handler = new Handler((_, _) => Json(response));
        using var http = new HttpClient(handler);
        using var client = new FoundryDecisionClient(http, options);

        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("text", "safe"));
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
