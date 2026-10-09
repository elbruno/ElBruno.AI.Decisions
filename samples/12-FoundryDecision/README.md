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
See [Program.cs](Program.cs), [all use cases](../../docs/use-cases.md),
[configuration](../../docs/configuration.md), and
[live integration tests](../../docs/testing.md).
