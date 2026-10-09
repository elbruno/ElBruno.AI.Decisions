using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Experimental Foundry client using the OpenAI Decisions contract as a provisional reference.</summary>
/// <remarks>
/// This is not a confirmed Microsoft-Decision-1 contract. Replace the isolated protocol when official
/// Foundry documentation is available.
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
        var question = FoundryProtocol.Question("choice", instructions);
        question["choices"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject
        {
            ["value"] = o.Key,
            ["description"] = o.Value
        })]);
        JsonElement answer = await EvaluateAsync(input, question, cancellationToken).ConfigureAwait(false);
        Dictionary<string, double> probabilities = FoundryProtocol.Distribution(answer, options.Keys.ToArray());
        string choice = FoundryProtocol.String(answer, "choice");
        if (!probabilities.ContainsKey(choice)) throw new DecisionException("The decision choice is not a supplied option.");
        return new ChoiceDecision(choice, probabilities, FoundryProtocol.Probability(answer, "confidence"));
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var question = FoundryProtocol.Question("score", instructions);
        question["levels"] = new JsonArray([.. rubric.Select(text => (JsonNode)new JsonObject { ["label"] = text })]);
        JsonElement answer = await EvaluateAsync(input, question, cancellationToken).ConfigureAwait(false);
        string[] indices = Enumerable.Range(0, rubric.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        Dictionary<string, double> probabilities = FoundryProtocol.Distribution(answer, indices, numericValues: true);
        var result = new ScoreDecision([.. indices.Select(i => probabilities[i])]);
        if (!answer.TryGetProperty("score", out JsonElement score) || !score.TryGetDouble(out double value) ||
            !double.IsFinite(value) || Math.Abs(value - result.Score) > 1e-6)
            throw new DecisionException("The decision score does not match its level probabilities.");
        FoundryProtocol.Probability(answer, "confidence");
        return result;
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        JsonElement answer = await EvaluateAsync(input, FoundryProtocol.Question("predicate", proposition), cancellationToken).ConfigureAwait(false);
        return new AssessmentDecision(FoundryProtocol.Probability(answer, "probability"));
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
        // Foundry resource roots provisionally use the OpenAI-compatible Decisions route.
        if (endpoint.AbsolutePath == "/") endpoint = new Uri(endpoint, "openai/v1/decisions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(new JsonObject
            {
                ["model"] = _options.Model,
                ["input"] = input,
                ["questions"] = new JsonArray(question)
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

        return FoundryProtocol.Answer(body, question["type"]!.GetValue<string>());
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

internal static class FoundryProtocol
{
    internal static JsonObject Question(string type, string instructions) => new()
    {
        ["type"] = type,
        ["name"] = "decision",
        ["instructions"] = instructions
    };

    internal static JsonElement Answer(string body, string expectedType)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("answers", out JsonElement answers) ||
                answers.ValueKind != JsonValueKind.Array || answers.GetArrayLength() != 1)
                throw new DecisionException("Expected exactly one decision answer.");
            JsonElement answer = answers[0];
            if (answer.ValueKind != JsonValueKind.Object)
                throw new DecisionException("The decision answer must be an object.");
            string type = String(answer, "type");
            if (type == "refusal") throw new DecisionException("The decision model refused the request.");
            if (type != expectedType || String(answer, "name") != "decision")
                throw new DecisionException("The decision answer does not match the requested question.");
            return answer.Clone();
        }
        catch (JsonException ex)
        {
            throw new DecisionException("The Foundry response was not valid JSON.", ex);
        }

    }

    internal static string String(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new DecisionException($"Missing or invalid decision field '{property}'.");
        return value.GetString()!;
    }

    internal static double Probability(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out JsonElement node) || node.ValueKind != JsonValueKind.Number ||
            !node.TryGetDouble(out double value) || !double.IsFinite(value) || value is < 0 or > 1)
            throw new DecisionException($"Missing or invalid decision probability '{property}'.");
        return value;
    }

    internal static Dictionary<string, double> Distribution(JsonElement answer, string[] labels, bool numericValues = false)
    {
        if (!answer.TryGetProperty("probabilities", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
            throw new DecisionException("Missing decision probability distribution.");
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string label;
            if (numericValues)
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("value", out JsonElement value) ||
                    value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int index))
                    throw new DecisionException("Invalid decision level index.");
                label = index.ToString(CultureInfo.InvariantCulture);
            }
            else label = String(entry, "value");
            if (!labels.Contains(label, StringComparer.Ordinal) || !result.TryAdd(label, Probability(entry, "probability")))
                throw new DecisionException("Unexpected or duplicate decision option.");
        }
        if (result.Count != labels.Length || Math.Abs(result.Values.Sum() - 1) > 1e-6)
            throw new DecisionException("The decision probabilities must cover all options and sum to one.");
        return result;
    }
}
