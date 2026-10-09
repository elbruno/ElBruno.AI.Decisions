using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Calls Microsoft-Decision-1 through the Microsoft Foundry SystemOne API.</summary>
/// <remarks>
/// Uses the Microsoft provider route and named TypeSafe-style questions documented in the Foundry launch article.
/// </remarks>
public sealed class FoundryDecisionClient : IDecisionClient, IDisposable
{
    private const int MaxResponseBytes = 1 << 20;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly FoundryDecisionOptions _options;
    private readonly Azure.Core.TokenCredential? _credential;

    /// <summary>Creates a client with its own HTTP transport.</summary>
    public FoundryDecisionClient(FoundryDecisionOptions options)
        : this(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), options, ownsHttp: true) { }

    /// <summary>Creates a client over a caller-managed HTTP client.</summary>
    public FoundryDecisionClient(HttpClient httpClient, FoundryDecisionOptions options) : this(httpClient, options, ownsHttp: false) { }

    private FoundryDecisionClient(HttpClient httpClient, FoundryDecisionOptions options, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        string[] errors = options.ValidationErrors();
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors), nameof(options));
        _http = httpClient;
        _ownsHttp = ownsHttp;
        _options = options;
        _credential = string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.Credential ?? new Azure.Identity.AzureCliCredential()
            : null;
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public async Task<ChoiceDecision> ChooseAsync(
        string input, string instructions, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Choice(input, instructions, options);
        var question = SystemOneProtocol.Question("choice", instructions);
        question["criteria"] = new JsonObject(options.Select(o => new KeyValuePair<string, JsonNode?>(o.Key, JsonValue.Create(o.Value))));
        JsonElement answer = await EvaluateAsync(input, question, cancellationToken).ConfigureAwait(false);
        Dictionary<string, double> probabilities = SystemOneProtocol.Distribution(answer, options.Keys.ToArray());
        string choice = SystemOneProtocol.String(answer, "choice");
        if (!probabilities.ContainsKey(choice)) throw new DecisionException("The decision choice is not a supplied option.");
        return new ChoiceDecision(choice, probabilities, SystemOneProtocol.Probability(answer, "confidence"));
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var question = SystemOneProtocol.Question("score", instructions);
        question["criteria"] = new JsonArray([.. rubric.Select(text => (JsonNode?)JsonValue.Create(text))]);
        JsonElement answer = await EvaluateAsync(input, question, cancellationToken).ConfigureAwait(false);
        string[] indices = Enumerable.Range(0, rubric.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        Dictionary<string, double> probabilities = SystemOneProtocol.Distribution(answer, indices);
        var result = new ScoreDecision([.. indices.Select(i => probabilities[i])]);
        if (!answer.TryGetProperty("score", out JsonElement score) || !score.TryGetDouble(out double value) ||
            !double.IsFinite(value) || Math.Abs(value - result.Score) > 1e-6)
            throw new DecisionException("The decision score does not match its level probabilities.");
        SystemOneProtocol.Probability(answer, "confidence");
        return result;
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        JsonElement answer = await EvaluateAsync(input, SystemOneProtocol.Question("noul", proposition), cancellationToken).ConfigureAwait(false);
        return new AssessmentDecision(SystemOneProtocol.Probability(answer, "noul"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private async Task<JsonElement> EvaluateAsync(
        string input, JsonObject question, CancellationToken cancellationToken)
    {
        Uri endpoint = _options.Endpoint!;
        if (endpoint.AbsolutePath == "/") endpoint = new Uri(endpoint, "providers/microsoft/v1/systemone");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(new JsonObject
            {
                ["model"] = _options.Model,
                ["state"] = input,
                ["questions"] = new JsonObject { ["decision"] = question }
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        if (_credential is not null)
        {
            Azure.Core.AccessToken token = await _credential.GetTokenAsync(
                new Azure.Core.TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                timeout.Token).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        else if (string.Equals(_options.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
        else
        {
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeaderName, _options.ApiKey);
        }

        string body;
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            body = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DecisionException($"Foundry returned HTTP {(int)response.StatusCode}.") { StatusCode = (int)response.StatusCode };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("The Foundry request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionException("The Foundry request failed.", ex);
        }

        return SystemOneProtocol.Answer(body, question["type"]!.GetValue<string>());
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new DecisionException("The Foundry response is too large.");
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
