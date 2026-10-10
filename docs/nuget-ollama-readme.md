# ElBruno.AI.Decisions.Ollama

Native local System One decisions through Ollama and the shared .NET 10
`IDecisionClient` API: Choose, Score and Assess. Uses `/v1/systemone`,
not chat prompting or first-token logprobs.

```powershell
dotnet add package ElBruno.AI.Decisions.Ollama --version 0.6.2
ollama pull nimble
```

Requires a running **Ollama v0.35.0 or later** and a local System One decision
model such as `nimble`, Tev1, `clef`, or `clef-flash`, not a general-purpose
chat model. No local API key is required.

## Quickstart

```csharp
using ElBruno.AI.Decisions.Ollama;

using var client = new OllamaDecisionClient(new OllamaDecisionOptions
{
    Endpoint = new Uri("http://localhost:11434/"),
    Model = "nimble"
});
var teams = new Dictionary<string, string?>
{
    ["billing"] = "Invoices and payments",
    ["support"] = "Technical problems"
};
var result = await client.ChooseAsync(
    "Please correct my invoice.", "Which team should handle this request?", teams);
Console.WriteLine($"{result.Choice}: {result.Confidence:P1}");
```

**URL template:** `http://<ollama-host>:11434/v1/systemone`.
Resource roots append that path; full invocation URLs are preserved.

## Illustrative response (not a live test result)

```json
{
  "model": "nimble",
  "answers": {
    "decision": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.9, "support": 0.1 },
      "confidence": 0.531
    }
  },
  "usage": { "input_tokens": 174, "output_tokens": 1 }
}
```

Example output: `billing: 53.1%` (culture-dependent formatting).
Confidence measures distribution concentration, `1 - H(p) / ln(N)`,
not calibrated correctness or the selected-label probability.

Choice limits depend on the model (typically 2-26 options; API maximum 255).
Score supports 2-10 ordered rubric levels and preserves fractional zero-based
scores. Assess returns native Noul proposition probability without a hidden
boolean threshold. Text requests are limited to 64 KiB, never truncated.
The shared API exposes one text question per call, not images or batches.

**Tentative early access:** live local decision-model compatibility remains
unverified. Offline contract checks do not prove model behavior.
Native System One support requires 0.6.1 or later; 0.6.0 used the old
chat-logprob implementation.

[Complete sample](https://github.com/elbruno/ElBruno.AI.Decisions/tree/main/samples/11-OllamaLocal) |
[Official decision guide](https://docs.ollama.com/capabilities/decision) |
[System One API](https://docs.ollama.com/api/systemone) |
[Repository](https://github.com/elbruno/ElBruno.AI.Decisions)
