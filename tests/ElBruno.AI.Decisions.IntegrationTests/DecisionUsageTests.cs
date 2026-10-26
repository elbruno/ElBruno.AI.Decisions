using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ElBruno.AI.Decisions.Foundry;
using ElBruno.AI.Decisions.Jev;
using ElBruno.AI.Decisions.Ollama;

namespace ElBruno.AI.Decisions.IntegrationTests;

public sealed class DecisionUsageTests
{
    public static IEnumerable<object?[]> UsageCases()
    {
        foreach (string provider in new[] { "Foundry", "Ollama", "Jev" })
            foreach (string operation in new[] { "choice", "score", "noul" })
            {
                yield return [provider, operation, null, null, null, null, null, null];
                yield return [provider, operation, "null", null, null, null, null, null];
                yield return [provider, operation, "{}", null, null, null, null, null];
                yield return [provider, operation, """{"input_tokens":174,"output_tokens":1}""", 174L, 1L, null, null, null];
                yield return [provider, operation, """{"input_tokens":0,"output_tokens":null}""", 0L, null, null, null, null];
                yield return [provider, operation, """{"output_tokens":0,"total_tokens":0}""", null, 0L, 0L, null, null];
                yield return [provider, operation, """{"input_tokens":9223372036854775807,"output_tokens":1}""", long.MaxValue, 1L, null, null, null];
                yield return [provider, operation, """{"input_tokens":12,"output_tokens":3,"total_tokens":20,"reasoning_tokens":2,"cached_tokens":0}""", 12L, 3L, 20L, 2L, 0L];
                yield return [provider, operation, """{"input_tokens_details":{"cached_tokens":4},"output_tokens_details":{"reasoning_tokens":2},"future_tokens":99}""", null, null, null, 2L, 4L];
            }
    }

    [Theory]
    [MemberData(nameof(UsageCases))]
    public async Task AllProvidersAndOperationsPreserveReportedUsage(
        string provider, string operation, string? json, long? input, long? output, long? total, long? reasoning, long? cached)
    {
        using var http = new HttpClient(new Handler(Reply(operation, json)));
        IDecisionClient client = Create(provider, http);
        using var lifetime = client as IDisposable;
        DecisionUsage? usage = await EvaluateAsync(client, operation);
        if (json is null or "null")
        {
            Assert.Null(usage);
            return;
        }

        Assert.NotNull(usage);
        Assert.Equal(input, usage.InputTokens);
        Assert.Equal(output, usage.OutputTokens);
        Assert.Equal(total, usage.TotalTokens);
        Assert.Equal(reasoning, usage.ReasoningTokens);
        Assert.Equal(cached, usage.CachedTokens);
    }

    public static IEnumerable<object[]> InvalidUsageCases()
    {
        foreach (string provider in new[] { "Foundry", "Ollama", "Jev" })
            foreach (string json in new[]
            {
            "[]", "1", """{"input_tokens":-1}""", """{"output_tokens":1.5}""",
            """{"total_tokens":"3"}""", """{"total_tokens":9223372036854775808}""",
            """{"reasoning_tokens":true}""", """{"cached_tokens":-1}""",
            """{"input_tokens":1,"input_tokens":2}""", """{"input_tokens_details":[]}""",
            """{"output_tokens_details":{"reasoning_tokens":-1}}"""
        })
                yield return [provider, json];
    }

    [Theory]
    [MemberData(nameof(InvalidUsageCases))]
    public async Task MalformedUsageFailsExplicitly(string provider, string json)
    {
        using var http = new HttpClient(new Handler(Reply("noul", json)));
        IDecisionClient client = Create(provider, http);
        using var lifetime = client as IDisposable;
        if (provider == "Jev")
            await Assert.ThrowsAsync<JevProtocolException>(() => client.AssessAsync("t", "p"));
        else
            await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("t", "p"));
    }

    private static async Task<DecisionUsage?> EvaluateAsync(IDecisionClient client, string operation) => operation switch
    {
        "choice" => (await client.ChooseAsync("t", "q", new Dictionary<string, string?> { ["a"] = null, ["b"] = null })).Usage,
        "score" => (await client.ScoreAsync("t", "q", ["bad", "good"])).Usage,
        _ => (await client.AssessAsync("t", "p")).Usage
    };

    private static IDecisionClient Create(string provider, HttpClient http) => provider switch
    {
        "Foundry" => new FoundryDecisionClient(http, new FoundryDecisionOptions
        {
            Endpoint = new Uri("https://usage-tests.invalid/"),
            ApiKey = "synthetic-key"
        }),
        "Ollama" => new OllamaDecisionClient(http, new OllamaDecisionOptions { Model = "synthetic-model" }),
        _ => new JevDecisionClientAdapter(new JevClient(http, new JevClientOptions
        {
            Endpoint = new Uri("https://usage-tests.invalid/"),
            ApiKey = "synthetic-key",
            MaxRetries = 0
        }))
    };

    private static string Reply(string operation, string? usage)
    {
        string answer = operation switch
        {
            "choice" => """{"type":"choice","choice":"a","confidence":0.7,"probabilities":{"a":0.7,"b":0.3}}""",
            "score" => """{"type":"score","score":0.3,"confidence":0.7,"probabilities":{"0":0.7,"1":0.3},"legend":{"0":"bad","1":"good"}}""",
            _ => """{"type":"noul","noul":0.8}"""
        };
        string body = new JsonObject
        {
            ["model"] = "synthetic-model",
            ["answers"] = new JsonObject { ["decision"] = JsonNode.Parse(answer) }
        }.ToJsonString();
        return usage is null ? body : body[..^1] + ",\"usage\":" + usage + "}";
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
