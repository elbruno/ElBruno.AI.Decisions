using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Foundry;

/// <summary>Calls Microsoft-Decision-1 on Foundry. Score and Assess are expressed as fixed-option decisions.</summary>
/// <remarks>
/// The request and response shapes live in this class only. They follow the public description of the model
/// (a situation, a question, and fixed options in; a calibrated probability per option out) and must be
/// confirmed against a live deployment.
/// </remarks>
public sealed class FoundryDecisionClient : IDecisionClient, IDisposable
{
    private const int MaxResponseBytes = 1 << 20;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly FoundryDecisionOptions _options;

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
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public async Task<ChoiceDecision> ChooseAsync(
        string input, string instructions, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Choice(input, instructions, options);
        Dictionary<string, double> probabilities = await ScoreOptionsAsync(
            input, instructions, options.Select(o => (o.Key, o.Value)).ToArray(), cancellationToken).ConfigureAwait(false);
        string choice = probabilities.MaxBy(p => p.Value).Key;
        return new ChoiceDecision(choice, probabilities);
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        var levels = rubric.Select((text, i) => (i.ToString(CultureInfo.InvariantCulture), (string?)text)).ToArray();
        Dictionary<string, double> probabilities = await ScoreOptionsAsync(input, instructions, levels, cancellationToken).ConfigureAwait(false);
        return new ScoreDecision([.. levels.Select(l => probabilities[l.Item1])]);
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        (string, string?)[] yesNo = [("yes", null), ("no", null)];
        Dictionary<string, double> probabilities = await ScoreOptionsAsync(
            input, $"Is the following statement true? {proposition}", yesNo, cancellationToken).ConfigureAwait(false);
        return new AssessmentDecision(probabilities["yes"]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private async Task<Dictionary<string, double>> ScoreOptionsAsync(
        string situation, string question, (string Label, string? Description)[] options, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(FoundryProtocol.BuildRequest(_options.Model, situation, question, options), Encoding.UTF8, "application/json")
        };
        if (string.Equals(_options.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
        else
        {
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeaderName, _options.ApiKey);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
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

        return FoundryProtocol.ParseProbabilities(body, options.Select(o => o.Label).ToArray());
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
    internal static string BuildRequest(string model, string situation, string question, (string Label, string? Description)[] options)
    {
        var root = new JsonObject
        {
            ["model"] = model,
            ["situation"] = situation,
            ["question"] = question,
            ["options"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject
            {
                ["label"] = o.Label,
                ["description"] = o.Description
            })])
        };
        return root.ToJsonString();
    }

    internal static Dictionary<string, double> ParseProbabilities(string body, string[] labels)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new DecisionException("The Foundry response was not valid JSON.", ex);
        }

        var found = new Dictionary<string, double>(StringComparer.Ordinal);
        Collect(root, found);
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string label in labels)
        {
            if (!found.TryGetValue(label, out double p) || !double.IsFinite(p) || p is < 0 or > 1)
            {
                throw new DecisionException($"The Foundry response has no valid probability for option '{label}'.");
            }

            result[label] = p;
        }

        return result;
    }

    // Accepts {"probabilities":{label:p}} and arrays of {label|option|name|choice, probability|score|confidence}, nested anywhere.
    private static void Collect(JsonNode? node, Dictionary<string, double> found)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["probabilities"] is JsonObject map)
                {
                    foreach ((string key, JsonNode? value) in map)
                    {
                        if (TryNumber(value, out double p)) found[key] = p;
                    }
                }

                string? label = new[] { "label", "option", "name", "choice" }
                    .Select(k => obj[k] is JsonValue v && v.TryGetValue(out string? s) ? s : null).FirstOrDefault(s => s is not null);
                if (label is not null)
                {
                    foreach (string k in new[] { "probability", "score", "confidence" })
                    {
                        if (TryNumber(obj[k], out double p)) { found[label] = p; break; }
                    }
                }

                foreach ((_, JsonNode? child) in obj) Collect(child, found);
                break;
            case JsonArray array:
                foreach (JsonNode? child in array) Collect(child, found);
                break;
        }
    }

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        return node is JsonValue v && v.TryGetValue(out value);
    }
}
