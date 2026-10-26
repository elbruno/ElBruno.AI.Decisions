# Testing and validation status

## Offline suite

```powershell
dotnet build ElBruno.AI.Decisions.slnx -c Release
dotnet test ElBruno.AI.Decisions.slnx -c Release --no-build
dotnet run --project samples\03-ParallelDecisions -c Release --no-build -- --offline
```

Normal tests use instrumented handlers and deterministic chat clients, not Jev or another cloud model. Fixtures are authored from documented contracts; they are **not recorded evidence of server compatibility**.

Coverage excludes generated JSON metadata, not handwritten transport/mapping code. CI checks line/branch coverage and builds and runs every sample with explicit offline fixtures. Package checks restore the actual local `.nupkg` in an isolated consumer without project references. See the [release guide](releasing.md) for the package check script.

## Live suite: deliberately deferred for tentative 0.6.0/0.6.1

Live tests are skipped unless `JEV_RUN_LIVE=1`. Once enabled, a missing key is a failure, not a skip or empty pass.

Configure the shared development store locally:

```powershell
.\scripts\Set-JevUserSecrets.ps1
dotnet user-secrets set "Jev:DefaultModel" "jev-1.13.0" --project tests\ElBruno.AI.Decisions.Jev.IntegrationTests
$env:JEV_RUN_LIVE = "1"
dotnet test tests\ElBruno.AI.Decisions.Jev.IntegrationTests -c Release
Remove-Item Env:JEV_RUN_LIVE
```

The full live suite has **three HTTP attempts total**, runs sequentially, uses synthetic ticket data, disables retries, and applies a 30-second per-operation deadline. It does not run load tests, deliberately trigger rate limits, or assume cancellations are free.

One test lists models and sends a mixed Choice/Score/Noul request. The other probes structured input, nullable instructions/descriptions, and structured Score legends. Failures in disputed contract behavior must be investigated and documented, not hidden with catch-and-pass logic.

Assertions inspect schema, correlation, ranges, types, and reported metadata rather than exact nondeterministic wording or presumed model accuracy. No response/key is written to a recording.

## Current boundary

Live Jev compatibility remains unverified until this opt-in suite succeeds
with an official key. The maintainer has authorized tentative 0.6.0/0.6.1 without
waiting for those checks; no new service calls are part of its release pipeline.
Full generative-chat demos additionally require a separately configured chat
provider. No amount of fixture testing proves account access, pricing, model
availability, or the provider's undocumented behavior.

## Live Foundry test

The opt-in test in `tests/ElBruno.AI.Decisions.IntegrationTests` is skipped unless `DECISIONS_RUN_LIVE=1` and the Foundry user-secrets are configured.
The gpt-6-luna integration test is separately gated by
`DECISIONS_RUN_OPENAI_LIVE=1` and `Decisions:OpenAI:Endpoint`,
`Decisions:OpenAI:Model`, and optional `Decisions:OpenAI:ApiKey` user-secrets.
The Foundry unit tests also cover the Azure OpenAI Decisions request/response
contract, including text/image messages, multiple questions, answer-name
correlation, refusals, token usage, and the existing SystemOne compatibility.
On October 26, 2026, all three live Foundry integration tests passed using
Azure CLI/Entra authentication against deployments on the same Foundry resource:

| Deployment | API | Live coverage |
| --- | --- | --- |
| `Decision-1` (Microsoft-Decision-1) | `/providers/microsoft/v1/systemone` | Choice, Assess/Noul, Score |
| `gpt-6-luna` | `/openai/v1/decisions` | Predicate, Choice and Score in one request; token usage; mixed text/image input |

Only synthetic text and a generated 32-by-32 blank white PNG were submitted.
An initial image fixture was rejected as invalid image data; replacing it with
a valid PNG resolved the failure without changing the library protocol.
These checks exercised the current source through project references, not a
newly published NuGet package. They do not establish calibration or semantic
accuracy, and live refusal behavior and external HTTPS image downloads remain
unverified.

Use the resource-level endpoint, not `/api/projects/<project>`, for these
decision APIs. For Azure CLI authentication, leave the relevant API-key setting
empty. Live checks are opt-in and incur model usage charges.
See the [step-by-step setup and commands](../samples/12-FoundryDecision/README.md#7-run-opt-in-live-integration-tests).
The three tests make five inference requests in total. Keep both live gates
unset for ordinary regression runs. The source sample exercises both contracts
without service requests:

```powershell
dotnet run --project samples\12-FoundryDecision -- --offline
dotnet run --project samples\12-FoundryDecision -- --offline --openai
```

### Published 0.7.0 verification

On October 26, 2026, [publication run 40374040985](https://github.com/elbruno/ElBruno.AI.Decisions/actions/runs/40374040985)
passed Linux/Windows validation, uploaded all four packages and symbols, and
verified exact-version public NuGet restores and isolated consumers against
the release artifacts. Public indexing required a wait after upload.
A separate standalone consumer then restored `[0.7.0]` of the Foundry package
from nuget.org and compiled and ran sample 12's SystemOne and OpenAI offline
flows with no project references. The integrated release source also passed
all three live Foundry tests before publication. The standalone package runs
were synthetic, not additional live model calls.

## Live Ollama smoke test

On October 13, 2026, a standalone .NET 10 consumer referencing the exact public
NuGet package `ElBruno.AI.Decisions.Ollama` `[0.6.2]`, with no project reference,
passed live checks against Ollama `0.35.1` and `nimble:latest`.
The loaded assembly reported
`0.6.2+b1cfbb382428d4498230b031c628d294ff30e23e`.
The model digest was
`9b953de7a5336756ece1cb1e8632e374b3dbdabe3d02d405cf8291da2d43a131`.
Only synthetic ticket and password-help text was submitted.

| Check | Observed result |
| --- | --- |
| Choice: route a duplicate-charge refund | `billing`; probabilities 0.995459 billing, 0.001757 support, 0.002784 sales; confidence 0.970819 |
| Assess: customer requests a refund | Probability 0.996512 |
| Score: helpfulness on a three-level rubric | 1.985965 on the 0-2 scale; probabilities 0.003087, 0.007860, 0.989052 |
| Nonexistent model | HTTP 404 surfaced as `DecisionException` |
| Oversized text (70,000 ASCII characters) | `ArgumentException` before transmission |

The consumer asserted complete normalized Choice distributions, valid selected
labels, finite probability/confidence ranges and valid Score bounds. The SDK's
response validation also checked the native Score legend and expected score.
These results verify this package/model/server combination, not calibration,
general classification accuracy, other models/versions, images, batches,
cancellation, or production readiness.

The initial Windows server on port 11434 returned HTTP 500 because its
`llama-server.exe` runner was missing. A direct `/v1/systemone` request reproduced
the same failure independently of the SDK. Validation therefore used a separate
official Ollama `0.35.1` portable distribution on loopback port 11435, verified
against the release asset's SHA-256
`dc50b9ca7f9023c86525012632cd1615b093d0407987444a7f62ecab617e8e93`.
The shared installation was not modified, and the temporary server was stopped.
The model remains downloaded for subsequent use.

For an installation with this error, restore the matching official Ollama
runner distribution before retrying; changing the .NET client will not repair
the server. See the [Windows documentation](https://docs.ollama.com/windows)
and [official 0.35.1 release](https://github.com/ollama/ollama/releases/tag/v0.35.1).
The normal release pipeline continues to run offline, not these live checks.
