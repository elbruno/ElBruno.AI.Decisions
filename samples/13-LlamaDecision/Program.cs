using System.Net;
using System.Text;
using ElBruno.AI.Decisions.Llama;

// 1. Start Llama App with a decision model, or use --offline for synthetic responses.
string Arg(string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

bool offline = args.Contains("--offline", StringComparer.Ordinal);
using var http = offline
    ? new HttpClient(new OfflineHandler())
    : new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
http.Timeout = Timeout.InfiniteTimeSpan;
using var client = new LlamaDecisionClient(http, new()
{
    Endpoint = new Uri(Arg("--endpoint", "http://localhost:9931/")),
    Model = Arg("--model", "LiquidAI/d1-omni-600M-GGUF:Q8_0"),
    Timeout = TimeSpan.FromMinutes(3)
});

// 2. Ask several named questions in one pass over the same text.
var questions = new Dictionary<string, LlamaQuestion>
{
    ["team"] = new LlamaChoiceQuestion("Which team should handle the problem?", new Dictionary<string, string?>
    {
        ["plumbing"] = "Water leaks and drains",
        ["electrical"] = "Lights and wiring",
        ["other"] = "Other problems or unclear input"
    }),
    ["priority"] = new LlamaScoreQuestion("How urgent is the request?", ["Not urgent", "Soon", "Immediate"]),
    ["contact"] = new LlamaNoulQuestion("Does the customer request contact?")
};
LlamaDecisionResponse text = await client.DecideAsync(new("The kitchen tap is leaking. Please contact me.", questions));
Print("Text", text);

// 3. Read a local image, or supply --image path.png. Use a vision-capable model.
string imagePath = Arg("--image", Path.Combine(AppContext.BaseDirectory, "media", "red-square.png"));
byte[] image = await File.ReadAllBytesAsync(imagePath);
var imageRequest = new LlamaDecisionRequest("An image contains a colored shape.", new Dictionary<string, LlamaQuestion>
{
    ["color"] = new LlamaChoiceQuestion("What color is the square?", new Dictionary<string, string?>
    {
        ["red"] = "The square is red",
        ["blue"] = "The square is blue",
        ["unclear"] = "Cannot determine"
    })
})
{ Images = [new(MediaType(imagePath), image)] };
Print("Image", await client.DecideAsync(imageRequest));

// 4. Read audio directly without transcription, or supply --audio path.wav.
string audioPath = Arg("--audio", Path.Combine(AppContext.BaseDirectory, "media", "maintenance.wav"));
byte[] audio = await File.ReadAllBytesAsync(audioPath);
var audioRequest = new LlamaDecisionRequest("A tenant submitted a maintenance voice report.",
    new Dictionary<string, LlamaQuestion> { ["team"] = questions["team"] })
{
    Audio = [new(MediaType(audioPath), audio)]
};
Print("Audio", await client.DecideAsync(audioRequest));

// 5. Treat confidence as a model estimate, not a guarantee of correctness.
static void Print(string label, LlamaDecisionResponse response)
{
    Console.WriteLine($"{label}: model={response.Model ?? "(not reported)"}");
    foreach ((string name, LlamaAnswer answer) in response.Answers)
    {
        switch (answer)
        {
            case LlamaChoiceAnswer choice:
                Console.WriteLine($"  {name}: choice={choice.Choice}, confidence={Confidence(choice.Confidence)}");
                foreach ((string option, double probability) in choice.Probabilities)
                    Console.WriteLine($"    {option}: {probability:P1}");
                break;
            case LlamaScoreAnswer score:
                Console.WriteLine($"  {name}: score={score.Score:F2}, confidence={Confidence(score.Confidence)}");
                for (int level = 0; level < score.Probabilities.Count; level++)
                    Console.WriteLine($"    level {level}: {score.Probabilities[level]:P1}");
                break;
            case LlamaNoulAnswer noul:
                Console.WriteLine($"  {name}: probability of yes={noul.Probability:P1}");
                break;
        }
    }
}

static string Confidence(double? confidence) => confidence is double value ? $"{value:P1}" : "not reported";

static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".webp" => "image/webp",
    ".wav" => "audio/wav",
    ".mp3" => "audio/mpeg",
    ".ogg" => "audio/ogg",
    ".flac" => "audio/flac",
    _ => throw new ArgumentException("Unsupported file extension.")
};

sealed class OfflineHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = await request.Content!.ReadAsStringAsync(cancellationToken);
        string answers = body.Contains("\"color\"", StringComparison.Ordinal)
            ? """{"color":{"type":"choice","choice":"red","probabilities":{"red":0.8,"blue":0.1,"unclear":0.1}}}"""
            : body.Contains("\"priority\"", StringComparison.Ordinal)
                ? """{"team":{"type":"choice","choice":"plumbing","probabilities":{"plumbing":0.8,"electrical":0.1,"other":0.1},"confidence":0.8},"priority":{"type":"score","score":1,"probabilities":{"0":0.1,"1":0.8,"2":0.1},"confidence":0.8},"contact":{"type":"noul","noul":0.9}}"""
                : """{"team":{"type":"choice","choice":"plumbing","probabilities":{"plumbing":0.8,"electrical":0.1,"other":0.1},"confidence":0.8}}""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"model":"offline-fixture","answers":{{answers}}}""", Encoding.UTF8, "application/json")
        };
    }
}
