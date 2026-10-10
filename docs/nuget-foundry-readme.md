# ElBruno.AI.Decisions.Foundry

Use Microsoft-Decision-1 on Microsoft Foundry through the shared .NET 10
`IDecisionClient` API: Choose, Score and Assess. This is a community SDK,
not an OpenAI model or an official Microsoft SDK.

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
smoke test on October 9, 2026. CLI authentication has not been smoke-tested.
This does not establish calibration or production readiness.
The shared text API exposes one question per call, not images or batches.

[Complete sample](https://github.com/elbruno/ElBruno.AI.Decisions/tree/main/samples/12-FoundryDecision) |
[Configuration](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/configuration.md) |
[Microsoft-Decision-1](https://commandline.microsoft.com/microsoft-decision-1-model-foundry/) |
[Repository](https://github.com/elbruno/ElBruno.AI.Decisions)
