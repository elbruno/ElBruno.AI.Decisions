using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Ollama;

/// <summary>
/// Local decisions through Ollama. Options are lettered, the model answers with one letter, and the
/// probabilities come from the first token's log-probabilities, normalized across the option letters.
/// </summary>
public sealed class OllamaDecisionClient : IDecisionClient, IDisposable
{
    private const int MaxResponseBytes = 4 << 20;
    private const int MaxOptions = 26;
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
        if (options.Count > MaxOptions) throw new ArgumentOutOfRangeException(nameof(options), $"Ollama choices support at most {MaxOptions} options.");
        KeyValuePair<string, string?>[] items = [.. options];
        double[] p = await LetterProbabilitiesAsync(input, instructions, [.. items.Select(i => i.Value is null ? i.Key : $"{i.Key}: {i.Value}")], cancellationToken).ConfigureAwait(false);
        var distribution = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++) distribution[items[i].Key] = p[i];
        return new ChoiceDecision(distribution.MaxBy(d => d.Value).Key, distribution);
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(
        string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Score(input, instructions, rubric);
        return new ScoreDecision(await LetterProbabilitiesAsync(input, instructions, rubric, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        DecisionRequestValidation.Assess(input, proposition);
        double[] p = await LetterProbabilitiesAsync(input, $"Is the following statement true? {proposition}", ["yes", "no"], cancellationToken).ConfigureAwait(false);
        return new AssessmentDecision(p[0]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private async Task<double[]> LetterProbabilitiesAsync(string situation, string question, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder()
            .Append("Situation:\n").Append(situation).Append("\n\nQuestion: ").Append(question).Append("\n\nOptions:\n");
        for (int i = 0; i < options.Count; i++) prompt.Append((char)('A' + i)).Append(": ").Append(options[i]).Append('\n');
        prompt.Append("\nAnswer with only the letter of the best option.");

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = "You are a decision model. Reply with exactly one option letter." },
                new JsonObject { ["role"] = "user", ["content"] = prompt.ToString() }),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_predict"] = 1 },
            ["logprobs"] = true,
            ["top_logprobs"] = 20
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.Endpoint, "api/chat"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        string text;
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            text = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DecisionException($"Ollama returned HTTP {(int)response.StatusCode}.") { StatusCode = (int)response.StatusCode };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("The Ollama request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionException("The Ollama request failed. Is the server running?", ex);
        }

        return ParseDistribution(text, options.Count);
    }

    internal static double[] ParseDistribution(string json, int optionCount)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DecisionException("The Ollama response was not valid JSON.", ex);
        }

        if (root?["logprobs"] is not JsonArray { Count: > 0 } logprobs || logprobs[0]?["top_logprobs"] is not JsonArray candidates)
        {
            throw new DecisionException("The Ollama response has no log-probabilities. Use an Ollama version and model that support them.");
        }

        double[] weights = new double[optionCount];
        foreach (JsonNode? candidate in candidates)
        {
            string token = ((string?)candidate?["token"])?.Trim() ?? "";
            if (token.Length != 1 || candidate?["logprob"] is not JsonValue lp || !lp.TryGetValue(out double logprob)) continue;
            int index = char.ToUpperInvariant(token[0]) - 'A';
            if (index >= 0 && index < optionCount && double.IsFinite(logprob)) weights[index] += Math.Exp(logprob);
        }

        double total = weights.Sum();
        if (total <= 0) throw new DecisionException("The model did not answer with one of the option letters.");
        return [.. weights.Select(w => w / total)];
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
