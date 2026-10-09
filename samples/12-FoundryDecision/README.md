# Foundry decisions

Calls Microsoft-Decision-1 to classify a support request and assess a proposition.
Uses the documented `/providers/microsoft/v1/systemone` route.

## Configure

```powershell
az login
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:Endpoint" "https://<resource>.services.ai.azure.com"
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:Model" "<deployment-name>"
dotnet run --project samples\12-FoundryDecision
```

Use the deployment name, such as `msft-decision-1`, not necessarily the catalog
model name. Without an API key, the client uses `AzureCliCredential`.
To use a key, run `scripts\Set-FoundryUserSecrets.ps1` for masked input.
A configured `Decisions:Foundry:ApiKey` takes precedence; remove it to return
to Azure CLI authentication. User secrets are outside Git but are not encrypted.
Live requests incur model usage charges and require invocation permissions.

## Offline

```powershell
dotnet run --project samples\12-FoundryDecision -- --offline
```

This uses synthetic responses, not Microsoft-Decision-1.

Illustrative Choice response excerpt (values vary in live requests):

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

The SDK exposes `Choice = "billing"`, the complete `Probabilities` map, and
`Confidence = 0.95`. Confidence is separate from the selected label probability.
Assess returns `{"type":"noul","noul":0.98}` inside `answers.decision`,
exposed as `AssessmentDecision.Probability = 0.98`, not a Boolean.
See [Score and Ollama response examples](../../docs/use-cases.md#example-responses).

See [Program.cs](Program.cs), [all use cases](../../docs/use-cases.md),
[configuration](../../docs/configuration.md), and
[live integration tests](../../docs/testing.md).
