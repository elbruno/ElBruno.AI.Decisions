using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElBruno.AI.Decisions.Llama;

/// <summary>Calls Llama App's native System One endpoint.</summary>
public sealed class LlamaDecisionClient : IDecisionClient, IDisposable
{
    private const int MaxMediaBytes = 8 * 1024 * 1024;
    private const int MaxRequestBytes = 12 * 1024 * 1024;
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _endpoint;
    private readonly string? _model;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a client owning its transport.</summary>
    public LlamaDecisionClient(LlamaDecisionOptions options)
        : this(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }, options, true) { }

    /// <summary>Creates a client over a caller-owned HTTP client.</summary>
    public LlamaDecisionClient(HttpClient httpClient, LlamaDecisionOptions options) : this(httpClient, options, false) { }

    internal LlamaDecisionClient(HttpClient httpClient, LlamaDecisionOptions options, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Endpoint is null || !options.Endpoint.IsAbsoluteUri || options.Endpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(options.Endpoint.UserInfo) || !string.IsNullOrEmpty(options.Endpoint.Query) || !string.IsNullOrEmpty(options.Endpoint.Fragment))
            throw new ArgumentException("Endpoint must be an absolute HTTP(S) URL without credentials, query, or fragment.", nameof(options));
        if (options.Model is not null && string.IsNullOrWhiteSpace(options.Model))
            throw new ArgumentException("Model must be null or a nonempty model ID.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentException("Timeout must be positive and within the supported timer range.", nameof(options));
        _http = httpClient;
        _ownsHttp = ownsHttp;
        _endpoint = options.Endpoint.AbsolutePath == "/" ? new Uri(options.Endpoint, "v1/systemone") : options.Endpoint;
        _model = options.Model;
        _timeout = options.Timeout;
    }

    /// <inheritdoc />
    public async Task<ChoiceDecision> ChooseAsync(string input, string instructions, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken = default)
    {
        LlamaDecisionResponse response = await DecideAsync(new(input, new Dictionary<string, LlamaQuestion> { ["decision"] = new LlamaChoiceQuestion(instructions, options) }), cancellationToken).ConfigureAwait(false);
        var answer = (LlamaChoiceAnswer)response.Answers["decision"];
        return new ChoiceDecision(answer.Choice, answer.Probabilities, answer.Confidence, response.Usage);
    }

    /// <inheritdoc />
    public async Task<ScoreDecision> ScoreAsync(string input, string instructions, IReadOnlyList<string> rubric, CancellationToken cancellationToken = default)
    {
        LlamaDecisionResponse response = await DecideAsync(new(input, new Dictionary<string, LlamaQuestion> { ["decision"] = new LlamaScoreQuestion(instructions, rubric) }), cancellationToken).ConfigureAwait(false);
        return new ScoreDecision(((LlamaScoreAnswer)response.Answers["decision"]).Probabilities, response.Usage);
    }

    /// <inheritdoc />
    public async Task<AssessmentDecision> AssessAsync(string input, string proposition, CancellationToken cancellationToken = default)
    {
        LlamaDecisionResponse response = await DecideAsync(new(input, new Dictionary<string, LlamaQuestion> { ["decision"] = new LlamaNoulQuestion(proposition) }), cancellationToken).ConfigureAwait(false);
        return new AssessmentDecision(((LlamaNoulAnswer)response.Answers["decision"]).Probability, response.Usage);
    }

    /// <summary>Evaluates named questions in one request. Media is encoded as data URLs.</summary>
    public async Task<LlamaDecisionResponse> DecideAsync(LlamaDecisionRequest batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(batch.State);
        ArgumentNullException.ThrowIfNull(batch.Questions);
        if (batch.Questions.Count is < 1 or > 100) throw new ArgumentException("A batch needs 1-100 questions.", nameof(batch));
        long textBytes = Encoding.UTF8.GetByteCount(batch.State);
        var questions = new JsonObject();
        foreach ((string name, LlamaQuestion question) in batch.Questions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(question);
            ArgumentException.ThrowIfNullOrWhiteSpace(question.Instructions);
            textBytes += Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(question.Instructions);
            JsonObject item;
            switch (question)
            {
                case LlamaChoiceQuestion choice:
                    DecisionRequestValidation.Choice(batch.State, choice.Instructions, choice.Criteria);
                    foreach ((string label, string? description) in choice.Criteria)
                        textBytes += Encoding.UTF8.GetByteCount(label) + (description is null ? 0 : Encoding.UTF8.GetByteCount(description));
                    item = SystemOneProtocol.Question("choice", choice.Instructions);
                    item["criteria"] = new JsonObject(choice.Criteria.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value))));
                    break;
                case LlamaScoreQuestion score:
                    DecisionRequestValidation.Score(batch.State, score.Instructions, score.Criteria);
                    foreach (string level in score.Criteria) textBytes += Encoding.UTF8.GetByteCount(level);
                    item = SystemOneProtocol.Question("score", score.Instructions);
                    item["criteria"] = new JsonArray([.. score.Criteria.Select(x => (JsonNode?)JsonValue.Create(x))]);
                    break;
                case LlamaNoulQuestion noul:
                    DecisionRequestValidation.Assess(batch.State, noul.Instructions);
                    item = SystemOneProtocol.Question("noul", noul.Instructions);
                    break;
                default: throw new ArgumentException("Unsupported question type.", nameof(batch));
            }
            questions.Add(name, item);
        }
        if (textBytes > 1024 * 1024) throw new ArgumentException("Text fields must not exceed 1 MiB in total.", nameof(batch));

        var body = new JsonObject { ["state"] = batch.State, ["questions"] = questions };
        if (_model is not null) body["model"] = _model;
        int mediaBytes = 0;
        AddMedia(body, "images", batch.Images, ["image/png", "image/jpeg", "image/webp"], ref mediaBytes);
        AddMedia(body, "files", batch.Audio, ["audio/wav", "audio/mpeg", "audio/ogg", "audio/flac"], ref mediaBytes);
        string json = body.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes) throw new ArgumentException("The request exceeds 12 MiB.", nameof(batch));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new DecisionException($"Llama returned HTTP {(int)response.StatusCode}.") { StatusCode = (int)response.StatusCode };
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw new DecisionException("The Llama response is too large.");
                buffer.Write(chunk, 0, read);
            }
            return Parse(buffer.ToArray(), batch.Questions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DecisionException("The Llama request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new DecisionException("The Llama request failed. Is the server running?", ex);
        }
    }

    private static void AddMedia(JsonObject body, string field, IReadOnlyList<LlamaMedia> media, string[] allowed, ref int total)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (media.Count > 10) throw new ArgumentException("At most 10 files per media type are supported.");
        var values = new JsonArray();
        foreach (LlamaMedia item in media)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!allowed.Contains(item.MediaType, StringComparer.Ordinal)) throw new ArgumentException("Unsupported media type.");
            ArgumentNullException.ThrowIfNull(item.Data);
            if (item.Data.Length == 0 || item.Data.Length > MaxMediaBytes - total) throw new ArgumentException("Media must be nonempty and total at most 8 MiB.");
            total += item.Data.Length;
            values.Add($"data:{item.MediaType};base64,{Convert.ToBase64String(item.Data)}");
        }
        if (values.Count > 0) body[field] = values;
    }

    private static LlamaDecisionResponse Parse(byte[] data, IReadOnlyDictionary<string, LlamaQuestion> questions)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;
            CheckDuplicates(root);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("answers", out JsonElement answers) || answers.ValueKind != JsonValueKind.Object)
                throw new DecisionException("Missing Llama answers.");
            var result = new Dictionary<string, LlamaAnswer>(StringComparer.Ordinal);
            foreach (JsonProperty property in answers.EnumerateObject())
            {
                if (!questions.TryGetValue(property.Name, out LlamaQuestion? question)) throw new DecisionException("Unexpected Llama answer.");
                JsonElement answer = property.Value;
                string type = SystemOneProtocol.String(answer, "type");
                if (type == "refusal") throw new DecisionException("The decision model refused the request.");
                LlamaAnswer parsed = question switch
                {
                    LlamaChoiceQuestion choice when type == "choice" => ParseChoice(answer, choice),
                    LlamaScoreQuestion score when type == "score" => ParseScore(answer, score),
                    LlamaNoulQuestion when type == "noul" => new LlamaNoulAnswer(SystemOneProtocol.Probability(answer, "noul")),
                    _ => throw new DecisionException("The Llama answer type does not match the question.")
                };
                result.Add(property.Name, parsed);
            }
            if (result.Count != questions.Count) throw new DecisionException("A Llama answer is missing.");
            string? model = root.TryGetProperty("model", out _) ? SystemOneProtocol.String(root, "model") : null;
            return new LlamaDecisionResponse(model, result, DecisionUsageReader.Read(root));
        }
        catch (JsonException ex) { throw new DecisionException("The Llama response was not valid JSON.", ex); }
    }

    private static LlamaChoiceAnswer ParseChoice(JsonElement answer, LlamaChoiceQuestion question)
    {
        Dictionary<string, double> distribution = SystemOneProtocol.Distribution(answer, question.Criteria.Keys.ToArray(), 1e-3);
        string choice = SystemOneProtocol.String(answer, "choice");
        if (!distribution.ContainsKey(choice) || distribution[choice] < distribution.Values.Max())
            throw new DecisionException("The selected choice is not a highest-probability option.");
        return new LlamaChoiceAnswer(choice, distribution, OptionalConfidence(answer));
    }

    private static LlamaScoreAnswer ParseScore(JsonElement answer, LlamaScoreQuestion question)
    {
        string[] labels = Enumerable.Range(0, question.Criteria.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        Dictionary<string, double> probabilities = SystemOneProtocol.Distribution(answer, labels, 1e-3);
        if (!answer.TryGetProperty("score", out JsonElement node) || node.ValueKind != JsonValueKind.Number ||
            !node.TryGetDouble(out double score) || !double.IsFinite(score) || score < 0 || score > labels.Length - 1)
            throw new DecisionException("Invalid Llama score.");
        if (answer.TryGetProperty("legend", out JsonElement legend) &&
            (legend.ValueKind != JsonValueKind.Object || legend.EnumerateObject().Count() != labels.Length ||
             labels.Where((label, i) => SystemOneProtocol.String(legend, label) != question.Criteria[i]).Any()))
            throw new DecisionException("The score legend does not match the rubric.");
        return new LlamaScoreAnswer(score, labels.Select(label => probabilities[label]).ToArray(), OptionalConfidence(answer));
    }

    private static double? OptionalConfidence(JsonElement answer) =>
        answer.TryGetProperty("confidence", out _) ? SystemOneProtocol.Probability(answer, "confidence") : null;

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty item in element.EnumerateObject())
            {
                if (!names.Add(item.Name)) throw new DecisionException("The Llama response contains duplicate fields.");
                CheckDuplicates(item.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) CheckDuplicates(item);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
