# Changelog

## Unreleased

- Add the Azure OpenAI Decisions API for gpt-6-luna, with typed text/image input,
  multi-question responses, per-question refusals, and usage reporting. Preserve
  the Microsoft-Decision-1 SystemOne API as the Foundry provider default.
- Verify both decision APIs live with Azure CLI/Entra authentication on October
  26, 2026: Microsoft-Decision-1 Choice/Assess/Score and gpt-6-luna multi-question
  Predicate/Choice/Score, token usage, and text/image input.
- Expand sample 12 with independent SystemOne/OpenAI configuration, all three
  SystemOne operations, batch distributions, refusal handling, model/usage
  output, and both synthetic offline flows. Document step-by-step setup,
  image data URLs, live test commands, migration, and validation limits.
- Record the published Ollama 0.6.2 package's live Choice, Score and Assess smoke
  test with `nimble` on Ollama 0.35.1, including error checks and the incomplete
  Windows installation's missing-runner failure. No runtime or dependency changes.

## 0.6.3 - 2026-10-14 (tentative)

- Expose optional provider-reported `DecisionUsage` on Choice, Score and Assessment results from Foundry, Ollama and the Jev adapter, preserving existing constructor signatures.
- Preserve nullable input, output, total, reasoning and cached token counters, including reported zero. Never infer a service-reported total from input plus output.
- Preserve extended usage in native Jev results and tools, with reported totals in separately attributable chat metadata. Reject malformed counters explicitly.
- Add synthetic cross-provider protocol tests for all operations, missing/partial usage, large counts and compatibility. Document provider availability and cross-model token-count limitations.

## 0.6.2 - 2026-10-09 (tentative)

- Package dedicated core, Foundry and Ollama NuGet READMEs with correct installation commands, quickstarts, responses and documentation links instead of the Jev README.
- Validate each packaged README's exact title, installation version and provider-specific source.
- Remove the unsupported calibration claim from core package metadata. Runtime behavior is unchanged.

## 0.6.1 - 2026-10-09 (tentative)

- Correct Ollama to the documented `/v1/systemone` API for local decision models (Ollama v0.35.0+), replacing chat-logprob prompting. Preserve native confidence and probabilities, validate score legends and bounded text requests, and update samples and response examples. Live local decision-model compatibility remains unverified.
- Share System One protocol validation between Foundry and Ollama.
- Add provider response examples and README NuGet/download badges.

## Decisions package family 0.6.0 - 2026-10-09 (tentative)
- Foundry uses the documented `/providers/microsoft/v1/systemone` route and named Choice, Score and Noul questions. Replaces the provisional OpenAI protocol. All three operations passed a live API-key smoke test on October 9, 2026.

- Repository renamed from `ElBruno.AI.Jev` to `ElBruno.AI.Decisions`; a fresh set of packages starts. The old `ElBruno.AI.Jev` NuGet package has not yet been deprecated.
- New core package `ElBruno.AI.Decisions` with `IDecisionClient` and `DecisionRoutingChatClient`.
- Jev SDK moved to `ElBruno.AI.Decisions.Jev` with an `IDecisionClient` adapter.
- New `ElBruno.AI.Decisions.Foundry` (Microsoft-Decision-1, verified API-key smoke test) and `ElBruno.AI.Decisions.Ollama` (native System One in current source) providers.
- Samples 11 (Ollama) and 12 (Foundry).

## 0.6.0 - 2026-09-30

- Added explicit loopback-only local Laya configuration with optional bearer authentication,
  Laya auto-routing when no model is specified, and an explicit unsupported model-discovery result.

## 0.5.0 - 2026-09-22 (tentative)

First public release, explicitly authorized for early evaluation while live
service testing is deferred. **Live Jev compatibility is unverified.** NuGet
classifies the exact `0.5.0` version as stable, but the package is tentative
and is not a production-readiness claim. APIs may change before 1.0.

- Native .NET 10 client for official TypeSafe AI Jev Choice, Score, and Noul decisions, structured values, typed question handles, model discovery, and native metadata.
- Microsoft.Extensions.AI function tools, explicit chat routing, and input or buffered-output assessments around separately configured chat clients.
- Bounded HTTP transport, safe error messages, cancellation, explicit ownership, and conservative configurable retries.
- Ten runnable samples with explicit synthetic offline mode, deterministic contract/integration tests, and separately gated real-service tests.
- NuGet package validation, symbols, release automation using existing trusted publishing, and original t2i-generated branding.
- README badges for NuGet version/downloads, cross-platform CI, release workflow, .NET, license, and tentative/live-validation status.
- The HTTP user agent follows the SDK assembly version rather than a stale hardcoded development version.

The earlier `0.1.0-preview.1` was an unpublished development version. This
release does not claim semantic correctness or prompt-injection protection.
