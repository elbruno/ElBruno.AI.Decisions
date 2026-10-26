# ElBruno.AI.Decisions.Foundry

Use Microsoft-Decision-1 on Microsoft Foundry or gpt-6-luna through the Azure
OpenAI Decisions API with the shared .NET 10 `IDecisionClient`: Choose, Score
and Assess. This is a community SDK, not an official Microsoft SDK.

```powershell
dotnet add package ElBruno.AI.Decisions.Foundry --version 0.6.2
```

## Quickstart

Deploy Microsoft-Decision-1 in Foundry and run `az login` for the example's
default `AzureCliCredential` authentication:

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
    "Please correct my invoice.", "Which team should handle this request?", teams);
Console.WriteLine($"{result.Choice}: {result.Confidence:P1}");
```

**URL template:** `https://<resource>.services.ai.azure.com/providers/microsoft/v1/systemone`.
Resource roots append that path; full invocation URLs are preserved.
**Model:** your deployment name, for example `msft-decision-1`, not necessarily
the catalog name `Microsoft-Decision-1`.
For API-key authentication, supply `ApiKey` from application configuration;
it takes precedence over CLI authentication. Never hardcode credentials.

## Illustrative response

```json
{
  "answers": {
    "decision": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.99, "support": 0.01 },
      "confidence": 0.95
    }
  }
}
```

Example output: `billing: 95.0%` (culture-dependent formatting).
Confidence is distinct from the selected-label probability.

**Tentative early access:** Choice, Score and Assess passed a live API-key
smoke test on October 9, 2026. Current source also passed live Azure CLI/Entra
checks for Microsoft-Decision-1 and gpt-6-luna on October 26, 2026, including
gpt-6-luna multi-question scoring and text/image input. These source checks do
not validate a newly published package.
This does not establish calibration or production readiness. The provider-neutral
`IDecisionClient` methods expose one text question per call; use `DecideAsync`
for image input or multi-question requests.

[Complete sample](https://github.com/elbruno/ElBruno.AI.Decisions/tree/main/samples/12-FoundryDecision) |
[Configuration](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/configuration.md) |
[Microsoft-Decision-1](https://commandline.microsoft.com/microsoft-decision-1-model-foundry/) |
[Repository](https://github.com/elbruno/ElBruno.AI.Decisions)

## Azure OpenAI Decisions API

**Unreleased source addition:** the APIs in this section are not available in
NuGet 0.6.3. Run the repository sample through project references until the next
package release.

For `gpt-6-luna`, select the Azure OpenAI Decisions contract explicitly:

```csharp
using var client = new FoundryDecisionClient(new FoundryDecisionOptions
{
    Api = FoundryDecisionApi.OpenAIDecisions,
    Endpoint = new Uri("https://<resource>.openai.azure.com"),
    Model = "<gpt-6-luna-deployment>"
});
```

The provider appends `/openai/v1/decisions` to a resource root. The default
`SystemOne` mode remains for Microsoft-Decision-1. See
[Microsoft's Decisions API documentation](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/decisions).

For text/image input and multiple questions in one request, use the additive
`FoundryDecisionClient.DecideAsync` API. It returns typed answers matched by
question name, including per-question refusals and optional token usage. The
provider-neutral `IDecisionClient` convenience methods remain text-only and
send one question per request. See the
[complete Foundry sample](https://github.com/elbruno/ElBruno.AI.Decisions/tree/main/samples/12-FoundryDecision).

For example, after creating the OpenAI client above:

```csharp
FoundryDecisionResult result = await client.DecideAsync(
    FoundryDecisionInput.FromText("The package arrived with a cracked screen."),
    [
        new FoundryPredicateQuestion("damage", "Does the input describe product damage?"),
        new FoundryChoiceQuestion("team", "Which team should handle this?", [
            new FoundryDecisionChoice("support", "Product support"),
            new FoundryDecisionChoice("billing", "Payments and refunds")
        ])
    ]);

foreach (FoundryDecisionAnswer answer in result.Answers)
{
    if (answer is FoundryRefusalAnswer)
        Console.WriteLine($"{answer.Name}: refused, requires review");
    else if (answer is FoundryPredicateAnswer predicate)
        Console.WriteLine($"{predicate.Name}: {predicate.Probability:P1}");
    else if (answer is FoundryChoiceAnswer choice)
        Console.WriteLine($"{choice.Name}: {choice.Choice}");
}
if (result.Usage is { } usage)
    Console.WriteLine($"Reported total tokens: {usage.TotalTokens}");
```

Use 1-400 uniquely named questions. Results are returned in request order.
Missing usage remains null. For images, use
`FoundryDecisionInput.FromMessages` with `FoundryDecisionMessage.User` and
`FoundryDecisionContentPart.Text`/`Image`. HTTPS image URLs must be publicly
accessible; supported image data URLs are also accepted. The service allows
up to 128 images and 32 MB combined image data. The SDK checks image count
and URL shape, not decoding or combined byte size.

The walkthrough includes `az login`, separate `Decisions:Foundry` and
`Decisions:OpenAI` settings, `--openai`, image input, and both offline modes.
