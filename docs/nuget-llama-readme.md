# ElBruno.AI.Decisions.Llama

Use locally installed decision models through [Llama App](https://llama.app/docs/decision-models).
Start Llama App and load a decision model, then configure its model ID and endpoint:

```powershell
dotnet add package ElBruno.AI.Decisions.Llama --version 0.9.0
```

The package is not yet published; the command applies after release.

```csharp
using ElBruno.AI.Decisions.Llama;

using var client = new LlamaDecisionClient(new LlamaDecisionOptions
{
    Model = "LiquidAI/d1-omni-600M-GGUF:Q8_0"
});
var response = await client.DecideAsync(new("The tap is leaking.",
    new Dictionary<string, LlamaQuestion>
    {
        ["team"] = new LlamaChoiceQuestion("Which team?", new Dictionary<string, string?>
        {
            ["plumbing"] = "Water leaks", ["electrical"] = "Wiring"
        }),
        ["urgent"] = new LlamaNoulQuestion("Is water leaking?")
    }));
```

The default endpoint is `http://localhost:9931/`. Model IDs are available from
`GET /v1/models`. Omit `Model` only when the server already selects one. The
SDK never downloads models. `IDecisionClient` provides text-only operations;
`DecideAsync` supports named batches and image/audio data. See the repository's
Llama sample for file inputs.

Media is transmitted to the configured endpoint as base64 data URLs. Use a
trusted endpoint: a remote server receives your text and media. Requests allow
up to 8 MiB total media and 12 MiB encoded JSON; responses are limited to
4 MiB. Text fields are limited to 1 MiB total. Image types are PNG, JPEG and WebP; audio types are WAV, MP3, Ogg and
FLAC, subject to model/server support. The native batch score is preserved;
the shared `ScoreDecision.Score` is calculated from the returned distribution.
Caller-supplied `HttpClient.Timeout` may impose a shorter timeout than the
configured provider timeout. The client never disposes a caller-supplied
`HttpClient`. A caller-supplied handler controls redirects; disable automatic
redirects to avoid forwarding media to an unintended endpoint.
Confidence is not calibrated correctness. Validate behavior and thresholds
against your own data and use human review for uncertain decisions.
