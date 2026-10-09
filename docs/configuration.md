# Configuration and credentials

The SDK targets **official TypeSafe AI**, defaulting to `https://api.typesafe.ai`. Do not use an independent proxy credential or change the endpoint as an automatic fallback.

## Local Laya

[Laya](https://github.com/NandhaKishorM/laya) can expose its compatible `POST /v1/systemone`
endpoint locally. Install its server extra and start the server according to Laya's documentation;
then configure this explicit local-only mode:

```csharp
var client = new JevClient(new JevClientOptions
{
    UseLocalLaya = true,
    Endpoint = new Uri("http://127.0.0.1:8000")
});
```

This is intentionally distinct from `AllowInsecureLoopback`, which remains test-only. `UseLocalLaya`
allows HTTP only at a loopback origin and permits an empty `ApiKey`; it cannot be used to send
unauthenticated requests to a remote server. If Laya is started with `LAYA_API_KEY`, configure its
optional bearer token explicitly:

```csharp
ApiKey = Environment.GetEnvironmentVariable("LAYA_API_KEY") ?? ""
```

No official TypeSafe credential is selected, copied, or sent by local Laya mode. Avoid exposing a
Laya server beyond loopback unless you add your own secure reverse proxy and use a separate client
integration with a reviewed trust boundary.

Requests with no per-call `model` omit that wire field so Laya auto-routes to a loaded checkpoint.
Set `JevDecisionRequest`'s `model` only to a documented Laya checkpoint or alias when pinning is
required. Laya currently treats an unrecognized explicit value as auto-routing upstream; because it
does not publish a model discovery endpoint, the SDK cannot validate that value. `ListModelsAsync`
therefore throws `JevUnsupportedCapabilityException` with capability `model-discovery` without
issuing `GET /v1/models`.

The existing `01-HelloChoice` and `02-ScoreAndNoul` samples run unchanged against this client
configuration and return typed choice probabilities, score distributions/legends/confidence, and
noul probabilities. Laya responses may include `model` and optional `usage`; the SDK preserves and
validates both. First use can download or load checkpoints, so preloading trades startup time and
memory for avoiding cold-request latency. Treat output quality and confidence thresholds as
deployment-specific: evaluate them on labelled data before gating production behavior.

## Development

All samples and the live integration-test project share `UserSecretsId` **ElBruno.AI.Decisions.Jev.Development**:

```powershell
.\scripts\Set-JevUserSecrets.ps1
dotnet user-secrets set "Jev:DefaultModel" "jev-1.13.0" --project samples\01-HelloChoice
```

The setup script prompts for a masked key and sends JSON to `dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development` through standard input. It sets only `Jev:ApiKey`, preserving the existing model and other settings. Run it once for all ten samples and live integration tests; `-WhatIf` previews the target without prompting or writing anything.

Never put the key in chat, source, command arguments, or test recordings. User-secrets are outside the repository, but **are not encrypted** and are only for development. A plaintext representation is necessarily created briefly in process memory to pass it to Secret Manager. The library itself never loads user-secrets or environment variables.

The sample host loads user-secrets explicitly, then environment variables and command-line configuration. `--offline` is a separate explicit switch installing a synthetic HTTP handler; it never falls back to live calls or silently handles missing live credentials.

### HTTP 401 after saving a key

Successful secret setup confirms local storage, not service authentication. If
the official API returns `401 Unauthorized`, verify that the key is active and
comes from the [official TypeSafe dashboard](https://console.typesafe.ai/keys),
not the independent `jevtypesafeai.com` proxy. Rerun the masked setup script to
replace the development key. An existing `Jev__ApiKey` environment variable takes
precedence over user-secrets, so check for an unintended override without
printing its value. Do not retry repeatedly or send the key to another host.

## Dependency injection

```csharp
builder.Services.AddJev(options =>
{
    options.ApiKey = builder.Configuration["Jev:ApiKey"] ?? "";
    options.DefaultModel = JevModels.Version1_13_0;
});
```

Resolve `IJevDecisionClient` or `JevClient`. Options validate on startup and resolution. Errors name configuration fields, never the credential value.

`AddJev` registers one default singleton facade. It creates and disposes a factory HTTP client **per operation**, while `IHttpClientFactory` manages handlers. It does not capture one short-lived HTTP client indefinitely. The returned `IHttpClientBuilder` allows configuring handlers and logging. Authorization is set on each request, not a shared client's default headers.

For several independently configured accounts, explicitly construct/register separate `JevClient` instances with the desired service keys/lifetimes. Do not call `AddJev` repeatedly expecting independent accounts: it configures one default client.

## Explicit HTTP ownership

```csharp
using var http = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});
using var client = new JevClient(http, options); // borrowed by default
```

The constructor does not mutate the supplied client's timeout, base address, or default headers. Set `disposeHttpClient: true` only to transfer ownership. The parameterless-transport constructor owns its transport and disables redirects. Do not dispose a client while requests are running.

Clients snapshot options at construction. Rotate credentials by replacing the client/options registration through your application's lifecycle; mutating the original options object does not change an existing client.

## Production

Inject credentials from the application's controlled secret provider. In a host that reads environment variables, `Jev__ApiKey` maps to `Jev:ApiKey`. Avoid command-line credentials, which can appear in shell history/process listings.

An explicitly configured endpoint must be an HTTPS **origin**, without a path, user information, query, or fragment. Test-only HTTP requires both a loopback host and `AllowInsecureLoopback = true`. Do not derive endpoint overrides from untrusted requests.

The SDK has no native body logging. Applications should also configure HTTP/OTel logging to avoid sensitive headers and payloads. `RawRepresentation` and opt-in `IncludeErrorBody` are sensitive escape hatches, not safe default telemetry.

## Foundry and Ollama providers

### Microsoft Foundry SystemOne

The provider uses the route documented in the
[Microsoft launch article](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/introducing-microsoft-decision-1-in-microsoft-foundry-for-decision-and-classific/4562742):
`/providers/microsoft/v1/systemone`. HTTPS resource roots append this path;
full invocation URLs are used unchanged. Requests contain `model`, `state`,
and a named `questions` object. Choice uses a `criteria` map, Score an ordered
`criteria` list, and Assess a `noul` question. Responses contain a named `answers`
object. Choice and Score probabilities are maps, and Assess reads `noul`.
Provider choice and confidence are preserved. Invalid results fail explicitly.

Choice, Score and Assess were verified against a real Microsoft-Decision-1
deployment with API-key authentication on October 9, 2026. This smoke test does
not establish calibration or production readiness. Microsoft-Decision-1 is not
an OpenAI model; the former OpenAI and MAI Decisions routes have been removed.

Use the shared user-secrets ID `ElBruno.AI.Decisions.Jev.Development` for
`Decisions:Foundry:Endpoint` and `Decisions:Foundry:Model`.
`Decisions:Foundry:ApiKey` is optional: when supplied it takes precedence;
when absent or empty the Foundry client uses `AzureCliCredential` after `az login`,
requesting the `https://cognitiveservices.azure.com/.default` scope.
Your signed-in identity must have permission to invoke the deployed model.
The existing `Set-FoundryUserSecrets.ps1` interactive helper requires a key;
use `dotnet user-secrets set --id ElBruno.AI.Decisions.Jev.Development` directly
for Azure CLI authentication, and remove any previously configured API key.

- Foundry: user-secrets keys `Decisions:Foundry:Endpoint` (HTTPS resource root or full invocation URL), optional `Decisions:Foundry:ApiKey`, deployment name in `Decisions:Foundry:Model`, and optional `Decisions:Foundry:ApiKeyHeader`.
- Ollama: `OllamaDecisionOptions` (endpoint defaults to `http://localhost:11434/`, `Model` required, for example `nimble`). Requires Ollama v0.35.0+ and a local System One decision model. A resource root appends `/v1/systemone`; a full invocation URL is preserved. Local requests need no API key. Text request bodies must fit within 64 KiB; oversized input is rejected, never truncated. Confidence is distribution concentration, not calibrated correctness. See the [official guide](https://docs.ollama.com/capabilities/decision).

Foundry and Ollama return named `answers.decision` objects. Choice preserves
the selected label, complete probability map, and separate confidence; Score
returns the fractional expected zero-based rubric level; Assess reads the
native numeric `noul` probability. See [example responses](use-cases.md#example-responses).
Ollama's native System One correction requires version `0.6.1` or later;
the `0.6.0` package uses the previous chat-logprob implementation.
