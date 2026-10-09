using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Ollama;

/// <summary>
/// Calls local decision models through Ollama's native System One API (v0.35.0 or later).
/// </summary>
public sealed class OllamaDecisionClient : IDecisionClient, IDisposable
{
    private const int MaxResponseBytes = 4 << 20;
    private const int MaxRequestBytes = 64 << 10;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly OllamaDecisionOptions _options;

    /// <summary>Creates a client with its own HTTP transport.</summary>
    public OllamaDecisionClient(OllamaDecisionOptions options)
        : this(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), options, ownsHttp: true) { }

    /// <summary>Creates a client over a caller-managed HTTP client.</summary>
    public OllamaDecisionClient(HttpClient httpClient, OllamaDecisionOptions options) : this(httpClient, options, ownsHttp: false) { }

    private OllamaDecisionClient(HttpClient httpClient, OllamaDecisionOptions options, bool ownsHttp)
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
        var question = SystemOneProtocol.Question("choice", instructions);
        question["criteria"] = new JsonObject(options.Select(o => new KeyValuePair<string, JsonNode?>(o.Key, JsonValue.Create(o.Value))));
        JsonElement answer = await EvaluateAsync(input, question, cancellationToken).ConfigureAwait(false);
        Dictionary<string, double> distribution = SystemOneProtocol.Distribution(answer, options.Keys.ToArray(), 1e-3);
        string choice = SystemOneProtocol.String(answer, "choice");
        if (!distribution.ContainsKey(choice) || distribution[choice] < distribution.Values.Max())
            throw new DecisionException("The Ollama choice must be a highest-probability supplied option.");
        return new ChoiceDecision(choice, distribution, SystemOneProtocol.Probability(answer, "confidence"));
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
        Dictionary<string, double> distribution = SystemOneProtocol.Distribution(answer, indices, 1e-3);
        var result = new ScoreDecision([.. indices.Select(i => distribution[i])]);
        if (!answer.TryGetProperty("score", out JsonElement score) || score.ValueKind != JsonValueKind.Number ||
            !score.TryGetDouble(out double value) || !double.IsFinite(value) || Math.Abs(value - result.Score) > 1e-3)
            throw new DecisionException("The Ollama score does not match its level probabilities.");
        if (!answer.TryGetProperty("legend", out JsonElement legend) || legend.ValueKind != JsonValueKind.Object ||
            legend.EnumerateObject().Count() != rubric.Count ||
            indices.Where((index, i) => SystemOneProtocol.String(legend, index) != rubric[i]).Any())
            throw new DecisionException("The Ollama score legend does not match the requested rubric.");
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

    private async Task<JsonElement> EvaluateAsync(string input, JsonObject question, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["state"] = input,
            ["questions"] = new JsonObject { ["decision"] = question }
        };
        string json = body.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes)
            throw new ArgumentException("Ollama System One text requests must not exceed 64 KiB.", nameof(input));
        Uri endpoint = _options.Endpoint;
        if (endpoint.AbsolutePath == "/") endpoint = new Uri(endpoint, "v1/systemone");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        string text;
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DecisionException($"Ollama returned HTTP {(int)response.StatusCode}.") { StatusCode = (int)response.StatusCode };
            }
            text = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("The Ollama request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionException("The Ollama request failed. Is the server running?", ex);
        }

        return SystemOneProtocol.Answer(text, question["type"]!.GetValue<string>());
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new DecisionException("The Ollama response is too large.");
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
