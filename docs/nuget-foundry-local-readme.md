# ElBruno.AI.Decisions.Foundry.Local

Run the shared `IDecisionClient` API with a cached typed-decision model through the Microsoft Foundry Local SDK.

```powershell
dotnet add package ElBruno.AI.Decisions.Foundry.Local
```

Install Foundry Local and cache a supported model, for example:

```powershell
foundry model download kev-0.8b
```

The CLI currently reports KEV models as `Other` and the SDK public catalog does not classify them as decision models. The resolver checks the model's `noul`, `choice`, and `score` capability metadata. When necessary, it registers the existing assets in the SDK local catalog under a distinct `elbruno-decisions-...-typed-decision` alias, without copying or downloading model files. It reuses a matching registration and does not unregister it on disposal, since other callers can use it. Existing registrations are never removed; a duplicate public alias left by another tool or an earlier implementation remains ambiguous in the CLI until its owner addresses it. If the SDK package metadata forces a different alias, the resolver fails before registration rather than risking a collision.

Initialize the SDK with the same cache used by the CLI (`AppName = "foundry"` selects Foundry Local's default cache), then resolve and use the model:

```csharp
using ElBruno.AI.Decisions.Foundry.Local;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

await FoundryLocalManager.CreateAsync(
    new Configuration { AppName = "foundry" },
    NullLogger.Instance);
FoundryLocalManager manager = FoundryLocalManager.Instance;
IModel model = await FoundryLocalDecisionModelResolver.ResolveAsync("kev-0.8b");

try
{
    var client = new FoundryLocalDecisionClient(model);
    AssessmentDecision result = await client.AssessAsync(
        "A customer cannot sign in and requests a password reset.",
        "Does this request concern account access?");
    Console.WriteLine(result.Probability);
}
finally
{
    await model.UnloadAsync();
    manager.Dispose();
}
```

The client loads the model on first use and does not own the manager or model lifecycle. The manager must remain alive while requests run. A model must already be cached; resolve fails explicitly rather than downloading potentially large assets. The SDK's `DecisionSession` validates typed-decision requests and answers. Since Foundry Local reports billing units rather than token counts, returned shared decisions leave token usage unknown.

Verified support: `kev-0.8b` ran CPU inference with Foundry Local SDK 2.4.1 on Windows. The same capability-checked adapter accepts other cached catalog models that advertise all three decision capabilities; `kev-4b` is not yet inference-verified. `clm-v0.1-8b` is not a decision model (its catalog capabilities are candidate-ranking, structured-input, and action-cache) and is rejected by the resolver. Hardware variants, other platforms, and calibration/accuracy are not covered by the smoke test.

Foundry Local SDK 2.4.1 is pinned to the verified local runtime contract. The existing remote provider remains in `ElBruno.AI.Decisions.Foundry`; install this separate package only for local typed decisions.
