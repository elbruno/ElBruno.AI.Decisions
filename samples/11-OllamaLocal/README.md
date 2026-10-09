# Local Ollama decisions

Demonstrates support routing (Choice), prompt-injection proposition assessment
(Assess), and answer helpfulness grading (Score).

## Run locally

```powershell
ollama pull llama3.2
dotnet run --project samples\11-OllamaLocal -- --model llama3.2 --endpoint http://localhost:11434/
```

The Ollama server must already be running, and the server/model must support
token logprobs. The client calls `/api/chat`, asks for a single letter, and
normalizes first-token probabilities across the supplied option letters.
Choice supports at most 26 options.

## Offline

```powershell
dotnet run --project samples\11-OllamaLocal -- --offline
```

Offline probabilities are synthetic and make no network calls.
Local model probabilities are not necessarily calibrated. The injection example
is illustrative, not a security guarantee.

See [Program.cs](Program.cs), [all use cases](../../docs/use-cases.md),
[configuration](../../docs/configuration.md), and
[testing](../../docs/testing.md).
