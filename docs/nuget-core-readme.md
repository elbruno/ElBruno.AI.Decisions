# ElBruno.AI.Decisions

Provider-neutral .NET 10 abstractions for decision models. Use one
`IDecisionClient` API to Choose a label, Score against an ordered rubric, or
Assess a proposition. Results preserve probabilities and provider confidence;
neither is a guarantee of correctness.

```powershell
dotnet add package ElBruno.AI.Decisions --version 0.6.3
```

This package contains abstractions and `DecisionRoutingChatClient`, not a model
or an HTTP provider. Install the provider that matches your deployment:

| Provider | Package | Service |
| --- | --- | --- |
| Foundry | [ElBruno.AI.Decisions.Foundry](https://www.nuget.org/packages/ElBruno.AI.Decisions.Foundry) | Microsoft-Decision-1 |
| Ollama | [ElBruno.AI.Decisions.Ollama](https://www.nuget.org/packages/ElBruno.AI.Decisions.Ollama) | Local System One decision models |
| Jev | [ElBruno.AI.Decisions.Jev](https://www.nuget.org/packages/ElBruno.AI.Decisions.Jev) | TypeSafe AI Jev |

## Shared API example

The application supplies an `IDecisionClient` from one of those providers:

```csharp
using ElBruno.AI.Decisions;

static async Task<string> RouteAsync(IDecisionClient client, string request)
{
    var teams = new Dictionary<string, string?>
    {
        ["billing"] = "Invoices and payments",
        ["support"] = "Technical problems"
    };
    var result = await client.ChooseAsync(
        request, "Which team should handle this request?", teams);
    return result.Choice;
}
```

For example, `"Please correct my invoice."` may return `billing`. Actual results
depend on the provider, model and input.

## Provider-reported usage

All three result types expose optional `Usage` (`DecisionUsage`) with nullable
`InputTokens`, `OutputTokens`, `TotalTokens`, `ReasoningTokens` and `CachedTokens`.
Missing usage is null; missing fields are null, while reported zero stays zero.
`TotalTokens` is only a provider-reported total, never input plus output.
Foundry and Ollama preserve native System One `usage`; the Jev adapter preserves
native Jev usage. Availability depends on the endpoint and model. Token counts
across models do not imply equivalent computation or cost.
See [usage semantics](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/decisions.md#provider-reported-token-usage).

**Tentative early access:** evaluate before production use. Foundry Choice,
Score and Assess passed a live API-key smoke test. Published Ollama `0.6.2`
passed a live Choice, Score and Assess smoke test with `nimble` on Ollama
`0.35.1` on October 13, 2026. Other Ollama models and live Jev compatibility
remain unverified. These checks do not establish calibration or production
readiness. APIs may change before 1.0.

[Repository and quickstarts](https://github.com/elbruno/ElBruno.AI.Decisions) |
[Use cases and response examples](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/use-cases.md) |
[Configuration](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/configuration.md)
