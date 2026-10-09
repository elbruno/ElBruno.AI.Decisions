# ElBruno.AI.Decisions.Jev

A community .NET 10 client for the official TypeSafe AI Jev service. It is one provider of the ElBruno.AI.Decisions family, which also includes `ElBruno.AI.Decisions` (provider-neutral `IDecisionClient`), `ElBruno.AI.Decisions.Foundry` (Microsoft-Decision-1) and `ElBruno.AI.Decisions.Ollama` (local models).

> **Tentative early-access release (0.6.0).** Live service compatibility remains
> unverified, and live testing is deferred. Offline test and package validation
> success does not prove real-service behavior. Evaluate before production use;
> APIs may change before 1.0. NuGet classifies `0.6.0` as stable because it has
> no prerelease suffix, but this package is still explicitly tentative.

```powershell
dotnet add package ElBruno.AI.Decisions.Jev --version 0.6.0
```

## Capabilities

The Foundry package is experimental: its provisional implementation follows the
[OpenAI Decisions specification](https://developers.openai.com/api/docs/guides/decisions),
not a confirmed Microsoft-Decision-1 contract. Resource roots provisionally use
`/mai/v1/decisions` (unverified, a live probe returned HTTP 404).
Microsoft-Decision-1 is a Microsoft model; OpenAI is only the payload reference.
Do not treat offline tests as live compatibility evidence.

- Choice classification with full probability distributions and confidence.
- Score evaluation against ordered rubrics, preserving fractional results and legends.
- Noul proposition probability without hidden boolean thresholds.
- Structured state and criteria, typed question handles, model discovery and pinning.
- Explicit loopback-only local Laya inference with optional bearer authentication.
- Microsoft.Extensions.AI function tools, chat routing, and assessment composition.
- Async HTTP, cancellation, bounded responses, explicit errors and controlled retries.

Jev is not a generative chat or embedding service. This package does not invent image, speech, or realtime endpoints. It is not an official TypeSafe AI SDK and does not target the independent jevtypesafeai.com proxy.

## Quickstart

```csharp
using ElBruno.AI.Decisions.Jev;

using var client = new JevClient(new JevClientOptions
{
    ApiKey = configuration["Jev:ApiKey"]
        ?? throw new InvalidOperationException("Configure Jev:ApiKey."),
    DefaultModel = JevModels.Version1_13_0
});
var question = new JevQuestionKey<JevNoulAnswer>("relevant");
var request = new JevDecisionRequest("A .NET developer asks about dependency injection.")
    .WithQuestion(question, new JevNoulQuestion("Is this about .NET development?"));
var response = await client.EvaluateAsync(request, cancellationToken);
Console.WriteLine(response.GetAnswer(question).Probability);
```

The consuming application supplies `configuration` and `cancellationToken`. Use .NET user-secrets for development or your application's production secret provider. Do not hardcode API keys.

Microsoft.Extensions.AI integrations wrap your own chat clients; a Jev credential does not enable another provider. Review configured confidence thresholds and model versions before making consequential decisions. Assessments are not security guarantees.

Official service documentation: https://docs.typesafe.ai/

The maintainer has authorized this tentative release before live verification.
See [the project repository](https://github.com/elbruno/ElBruno.AI.Decisions) for runnable
samples, configuration, tests, known limitations, and release instructions.
