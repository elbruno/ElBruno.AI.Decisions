# Local Ollama decisions

Demonstrates support routing (Choice), prompt-injection proposition assessment
(Assess), and answer helpfulness grading (Score).

## Run locally

```powershell
ollama pull nimble
dotnet run --project samples\11-OllamaLocal -- --model nimble --endpoint http://localhost:11434/
```

Requires a running Ollama v0.35.0+ server and a local System One decision model.
The client calls `/v1/systemone` with `state` and named `questions`.
General chat models such as `llama3.2` and cloud models are not supported.
Local requests need no API key. Choice limits depend on the model (typically
2-26 options, API maximum 255); the shared Score API supports 2-10 levels.
Text request bodies must fit within 64 KiB.
This correction is currently source-only, not in the published NuGet `0.6.0`.

## Offline

```powershell
dotnet run --project samples\11-OllamaLocal -- --offline
```

Offline probabilities are synthetic and make no network calls.
Confidence measures distribution concentration, not correctness. The injection example
is illustrative, not a security guarantee.

Example offline output:

```text
Route: billing (65%)
  billing  90.0%
  support  7.0%
  sales    3.0%
Prompt injection probability: 98%
Helpfulness: 1.70 of 2
```

Formatting follows your current culture. The synthetic Choice response is:

```json
{
  "model": "nimble",
  "answers": {
    "decision": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.9, "support": 0.07, "sales": 0.03 },
      "confidence": 0.6476
    }
  },
  "usage": { "input_tokens": 100, "output_tokens": 1 }
}
```

See the [official guide](https://docs.ollama.com/capabilities/decision),
[System One API](https://docs.ollama.com/api/systemone), and
[Score/Assess response examples](../../docs/use-cases.md#example-responses).

See [Program.cs](Program.cs), [all use cases](../../docs/use-cases.md),
[configuration](../../docs/configuration.md), and
[testing](../../docs/testing.md).
