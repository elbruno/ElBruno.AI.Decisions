# Foundry decisions, step by step

This .NET 10 sample supports two distinct model contracts. The Azure OpenAI
Decisions additions are currently **unreleased source**, not part of NuGet
0.6.3. Run this checkout with project references to try them.

| Mode | Deployment example | Route | Sample behavior |
| --- | --- | --- | --- |
| Default `SystemOne` | `Decision-1` (Microsoft-Decision-1) | `/providers/microsoft/v1/systemone` | Separate Choice, Assess/Noul, and Score calls |
| `--openai` | `gpt-6-luna` | `/openai/v1/decisions` | Choice, Predicate, and Score in one request; optional image; model and usage |

Use your actual **deployment names**, not necessarily the catalog model names.
"MAI-Decisions-1" refers here to Microsoft-Decision-1, not a third endpoint.

## 1. Check prerequisites

Install the .NET SDK selected by `global.json` and Azure CLI. Deploy the models
in a supported Foundry region and give your signed-in identity permission to
invoke them. From the repository root:

```powershell
az login
dotnet build samples\12-FoundryDecision
```

Use resource inference endpoints, not the Foundry project URL
`https://<resource>.services.ai.azure.com/api/projects/<project>`.
Do not append a decision route to that project URL.

## 2. Configure both deployments without overwriting each other

The sample and integration tests share the development user-secrets ID
`ElBruno.AI.Decisions.Jev.Development`:

```powershell
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:Endpoint" "https://<resource>.services.ai.azure.com"
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:Model" "Decision-1"
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:Api" "SystemOne"
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:OpenAI:Endpoint" "https://<resource>.openai.azure.com"
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:OpenAI:Model" "gpt-6-luna"
```

Without an API key, the client uses `AzureCliCredential`. A configured key takes
precedence. If a key was previously configured, remove only its setting to
return to CLI authentication:

```powershell
dotnet user-secrets remove --id ElBruno.AI.Decisions.Jev.Development "Decisions:Foundry:ApiKey"
dotnet user-secrets remove --id ElBruno.AI.Decisions.Jev.Development "Decisions:OpenAI:ApiKey"
```

An environment override such as `Decisions__OpenAI__ApiKey` also takes precedence
over user-secrets. Do not print credential values while diagnosing overrides.
For SystemOne key authentication, `scripts\Set-FoundryUserSecrets.ps1` prompts
for masked input. User-secrets are outside Git but **are not encrypted**.
Never put keys in source, chat, shell arguments, or recordings.

For compatibility, the sample still accepts the original single-section setup:
set `Decisions:Foundry:Api` to `OpenAIDecisions` with the OpenAI endpoint/model
under `Decisions:Foundry`. If `Decisions:OpenAI:Endpoint` exists, OpenAI mode
uses that entire section instead. The library itself does not load configuration.

## 3. Run Microsoft-Decision-1

```powershell
dotnet run --project samples\12-FoundryDecision
```

This makes three live requests: routing, proposition assessment, and rubric
scoring. Confidence is distinct from the selected option's probability.
Assess returns a probability in [0,1], not a Boolean. Scores are expected
zero-based rubric positions, not percentages.

## 4. Run the gpt-6-luna batch

```powershell
dotnet run --project samples\12-FoundryDecision -- --openai
```

`Program.cs` calls `DecideAsync` with named `team`, `refund`, and `priority`
questions. It prints typed answers, complete choice/score distributions, the
returned model, and optional token usage. Answers are correlated by question name
and returned in request order. A refusal affects its question, not every answer.
Missing usage is displayed as "not reported", never inferred as zero.

Each run incurs model usage charges. Treat confidence and application thresholds
as workload-specific, not calibrated correctness or automatic security gates.

## 5. Add an image

For a publicly accessible HTTPS image:

```powershell
dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development "Decisions:OpenAI:ImageUrl" "https://<public-host>/invoice.png"
dotnet run --project samples\12-FoundryDecision -- --openai
```

Use an image appropriate to the sample's invoice-routing questions. The service
must be able to download the image without cookies or credentials. Do not send
private documents without approval for the selected cloud destination.

Alternatively, pass a local PNG as a base64 data URL through an ephemeral
environment override (this sends the image to the model):

```powershell
$imagePath = (Resolve-Path .\invoice.png).Path
$env:Decisions__OpenAI__ImageUrl = 'data:image/png;base64,' +
    [Convert]::ToBase64String([IO.File]::ReadAllBytes($imagePath))
try {
    dotnet run --project samples\12-FoundryDecision -- --openai
} finally {
    Remove-Item Env:Decisions__OpenAI__ImageUrl
}
```

Use a valid PNG, JPEG, WebP, or GIF, with a matching data-URL MIME type.
The service limits a request to 128 images and 32 MB combined image data.
The library validates image count and URL shape, not image decoding or the
combined byte size. Invalid images fail at the service, not as successful
empty answers. Remove the optional image setting when finished:

```powershell
dotnet user-secrets remove --id ElBruno.AI.Decisions.Jev.Development "Decisions:OpenAI:ImageUrl"
```

## 6. Run without cloud calls

```powershell
dotnet run --project samples\12-FoundryDecision -- --offline
dotnet run --project samples\12-FoundryDecision -- --offline --openai
```

Both install synthetic handlers. Offline output is explicitly labeled and does
not demonstrate live compatibility or model accuracy. Neither run needs
credentials or a deployment.

## 7. Run opt-in live integration tests

After configuring both sections above:

```powershell
$env:DECISIONS_RUN_LIVE = '1'
$env:DECISIONS_RUN_OPENAI_LIVE = '1'
try {
    dotnet test tests\ElBruno.AI.Decisions.IntegrationTests --filter FullyQualifiedName~FoundryLiveTests
} finally {
    Remove-Item Env:DECISIONS_RUN_LIVE, Env:DECISIONS_RUN_OPENAI_LIVE
}
```

The three tests make five inference requests: three SystemOne requests, one
three-question OpenAI batch, and one OpenAI image request using a synthetic
blank white PNG. Missing required configuration fails once enabled.
All three tests passed with Azure CLI/Entra authentication on October 26, 2026.
They verify protocol compatibility, not calibration, semantic accuracy, or
production readiness. Live refusals and external HTTPS image downloads remain
unverified.

See [Program.cs](Program.cs), [API semantics](../../docs/decisions.md#foundry-azure-openai-decisions),
[use cases](../../docs/use-cases.md), [configuration](../../docs/configuration.md),
and [validation scope](../../docs/testing.md#live-foundry-test).
