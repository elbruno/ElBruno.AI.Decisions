# ElBruno.AI.Decisions

Provider-neutral .NET 10 abstractions for decision models. Use one
`IDecisionClient` API to Choose a label, Score against an ordered rubric, or
Assess a proposition. Results preserve probabilities and provider confidence;
neither is a guarantee of correctness.

```powershell
dotnet add package ElBruno.AI.Decisions --version 0.6.2
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

**Tentative early access:** evaluate before production use. Foundry Choice,
Score and Assess passed a live API-key smoke test. Live local Ollama
decision-model and Jev compatibility remain unverified. APIs may change before 1.0.

[Repository and quickstarts](https://github.com/elbruno/ElBruno.AI.Decisions) |
[Use cases and response examples](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/use-cases.md) |
[Configuration](https://github.com/elbruno/ElBruno.AI.Decisions/blob/main/docs/configuration.md)
