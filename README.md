# ElBruno.AI.Decisions

[![CI](https://github.com/elbruno/ElBruno.AI.Decisions/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/elbruno/ElBruno.AI.Decisions/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

One .NET API for **classification, routing, rubric scoring, and proposition
assessment**, with probability distributions instead of generated text.
Start with Microsoft-Decision-1 on **Foundry**, run models locally with
**Ollama**, or use the native **Jev** SDK.

| Package | Role |
| --- | --- |
| `ElBruno.AI.Decisions` | `IDecisionClient`, Choice/Score/Assessment results, Microsoft.Extensions.AI routing |
| `ElBruno.AI.Decisions.Foundry` | Microsoft-Decision-1 through Microsoft's SystemOne endpoint |
| `ElBruno.AI.Decisions.Ollama` | Local decisions from first-token log probabilities |
| `ElBruno.AI.Decisions.Jev` | Native TypeSafe AI Jev SDK and an `IDecisionClient` adapter |

**Early access:** version `0.6.0` is available on NuGet for
[core](https://www.nuget.org/packages/ElBruno.AI.Decisions/0.6.0),
[Foundry](https://www.nuget.org/packages/ElBruno.AI.Decisions.Foundry/0.6.0),
[Ollama](https://www.nuget.org/packages/ElBruno.AI.Decisions.Ollama/0.6.0), and
[Jev](https://www.nuget.org/packages/ElBruno.AI.Decisions.Jev/0.6.0).
See [release status and instructions](docs/releasing.md) for limitations.
The original `ElBruno.AI.Jev` package has **not yet been deprecated**.

## 1. Foundry: Microsoft-Decision-1

Reference package: `ElBruno.AI.Decisions.Foundry`.
This example classifies a request:

```csharp
using ElBruno.AI.Decisions.Foundry;

using var client = new FoundryDecisionClient(new FoundryDecisionOptions
{
    Endpoint = new Uri("https://<resource>.services.ai.azure.com"),
    Model = "<deployment-name>"
});

var teams = new Dictionary<string, string?>
{
    ["billing"] = "Invoices and payments",
    ["support"] = "Technical problems"
};
var result = await client.ChooseAsync(
    "Please correct my invoice.",
    "Which team should handle this request?",
    teams);

Console.WriteLine($"{result.Choice}: {result.Confidence:P1}");
```

**URL template:** `https://<resource>.services.ai.azure.com/providers/microsoft/v1/systemone`.
Resource roots automatically append that path; full invocation URLs are used
unchanged. **Model:** use your deployment name (for example, `msft-decision-1`),
not necessarily the catalog model name `Microsoft-Decision-1`.

The example uses `AzureCliCredential` after `az login`. Set `ApiKey` in the options
from configuration to use key authentication instead; a supplied key takes
precedence. Never hardcode credentials.

Choice, Score and Assess passed a live API-key smoke test on October 9, 2026.
That does not prove calibration or production readiness.
See the [complete Foundry sample](samples/12-FoundryDecision/README.md),
[configuration and user secrets](docs/configuration.md#foundry-and-ollama-providers),
and [Microsoft's endpoint example](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/introducing-microsoft-decision-1-in-microsoft-foundry-for-decision-and-classific/4562742).

## 2. Ollama: local decisions

Reference package: `ElBruno.AI.Decisions.Ollama`.
Start Ollama and pull a model, for example `ollama pull llama3.2`.
The same task runs locally:

```csharp
using ElBruno.AI.Decisions.Ollama;

using var client = new OllamaDecisionClient(new OllamaDecisionOptions
{
    Endpoint = new Uri("http://localhost:11434/"),
    Model = "llama3.2"
});

var teams = new Dictionary<string, string?>
{
    ["billing"] = "Invoices and payments",
    ["support"] = "Technical problems"
};
var result = await client.ChooseAsync(
    "Please correct my invoice.",
    "Which team should handle this request?",
    teams);

Console.WriteLine($"{result.Choice}: {result.Confidence:P1}");
```

**URL template:** `http://<ollama-host>:11434/` (the client calls `/api/chat`).
**Model:** an installed Ollama model name. Your server/model must return token
logprobs. Choices support up to 26 options, mapped to letters.
Probabilities are normalized across those letters; they are **not equivalent to
the calibrated probabilities of a dedicated decision model**.

See the [complete Ollama sample](samples/11-OllamaLocal/README.md) and
[provider configuration](docs/configuration.md#foundry-and-ollama-providers).

## 3. Jev: native decisions and advanced composition

Reference package: `ElBruno.AI.Decisions.Jev`. The native SDK preserves typed
question handles, structured JSON state, multiple questions per request, model
discovery, metadata, and Choice/Score/Noul results. `JevDecisionClientAdapter`
exposes the shared `IDecisionClient` API; `AddJev` registers it for DI.

Official TypeSafe endpoint: `https://api.typesafe.ai`.
Configure `Jev:ApiKey` using [user secrets](docs/configuration.md).
This community SDK is not an official TypeSafe SDK; the independent
`jevtypesafeai.com` proxy is not supported. Live TypeSafe compatibility remains
unverified. Loopback-only local Laya inference is also supported by the Jev SDK.

Start with [Hello Choice](samples/01-HelloChoice/README.md), then
[Score and Noul](samples/02-ScoreAndNoul/README.md) and
[dependency injection](samples/06-DependencyInjection/README.md).

## Use cases and runnable examples

The shared API provides `ChooseAsync`, `ScoreAsync`, and `AssessAsync`.
Read the [use-case guide](docs/use-cases.md) for examples in Foundry, Ollama,
and Jev order, and [decision semantics](docs/decisions.md) for native Jev details.

| Use case | Examples and documentation |
| --- | --- |
| Support classification and routing | [Foundry](samples/12-FoundryDecision/README.md), [Ollama](samples/11-OllamaLocal/README.md), [Jev Choice](samples/01-HelloChoice/README.md) |
| Quality grading and proposition assessment | [Shared API guide](docs/use-cases.md), [Jev Score/Noul](samples/02-ScoreAndNoul/README.md) |
| Multiple questions against shared evidence | [Parallel decisions](samples/03-ParallelDecisions/README.md) |
| Structured JSON input | [Structured state](samples/04-StructuredState/README.md) |
| Model discovery and pinning | [Jev models](samples/05-ModelsAndPinning/README.md) |
| Dependency injection and HTTP ownership | [DI sample](samples/06-DependencyInjection/README.md), [configuration](docs/configuration.md) |
| Decision tools for chat agents | [MEAI tools](samples/07-MEAI-Tools/README.md), [integration guide](docs/microsoft-extensions-ai.md) |
| Route to separately configured chat models | [MEAI routing](samples/08-MEAI-Routing/README.md), shared `DecisionRoutingChatClient` |
| Review chat inputs or buffered outputs | [MEAI assessments](samples/09-MEAI-Assessments/README.md) |
| RAG relevance and reranking | [RAG sample](samples/10-RagReranking/README.md) |

Samples 01-10 use the native Jev SDK. Samples 11-12 use the shared API.
Do not assume native Jev features such as model discovery exist in every provider.

## Run without credentials

```powershell
dotnet run --project samples\12-FoundryDecision -- --offline
dotnet run --project samples\11-OllamaLocal -- --offline
dotnet run --project samples\01-HelloChoice -- --offline
```

`--offline` uses explicitly labeled synthetic HTTP fixtures, not a model.
Missing credentials never silently select offline mode.
See [live and offline testing](docs/testing.md).

## Limits and development

Confidence is not correctness. Assessments are not guaranteed prompt-injection
protection. Calibrate automation thresholds on representative data and retain
human review for consequential decisions. The providers do not generate chat;
Microsoft.Extensions.AI routing wraps separately supplied chat clients.
Jev retry/transport policies are described in [errors and retries](docs/errors-and-retries.md)
and do not imply identical behavior in Foundry or Ollama.

Use the .NET 10 SDK pinned by `global.json`:

```powershell
dotnet build ElBruno.AI.Decisions.slnx -c Release
dotnet test ElBruno.AI.Decisions.slnx -c Release --no-build
```

See [contributing](CONTRIBUTING.md), [release preparation](docs/releasing.md),
and [image provenance](images/README.md). Licensed under [MIT](LICENSE).
