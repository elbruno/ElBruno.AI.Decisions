# Decisions and model semantics

## Foundry Azure OpenAI Decisions

The Foundry-specific API below is an unreleased addition after NuGet 0.6.3.
Use `FoundryDecisionApi.OpenAIDecisions` for gpt-6-luna. Microsoft-Decision-1
continues to use the default `SystemOne` mode; `DecideAsync` rejects that mode
rather than sending an incompatible payload.

```csharp
using ElBruno.AI.Decisions.Foundry;

using var client = new FoundryDecisionClient(new FoundryDecisionOptions
{
    Api = FoundryDecisionApi.OpenAIDecisions,
    Endpoint = new Uri("https://<resource>.openai.azure.com"),
    Model = "<gpt-6-luna-deployment>"
});

FoundryDecisionResult result = await client.DecideAsync(
    FoundryDecisionInput.FromText("The package arrived with a cracked screen."),
    [
        new FoundryPredicateQuestion("damage", "Does the input describe product damage?"),
        new FoundryChoiceQuestion("team", "Which team should handle this?", [
            new FoundryDecisionChoice("support", "Product support"),
            new FoundryDecisionChoice("billing", "Payments and refunds")
        ]),
        new FoundryScoreQuestion("severity", "How severe is the problem?", [
            new FoundryDecisionLevel("low", "Cosmetic only"),
            new FoundryDecisionLevel("high", "Product cannot be used")
        ])
    ]);

foreach (FoundryDecisionAnswer answer in result.Answers)
{
    switch (answer)
    {
        case FoundryPredicateAnswer predicate:
            Console.WriteLine($"{predicate.Name}: {predicate.Probability:P1}");
            break;
        case FoundryChoiceAnswer choice:
            Console.WriteLine($"{choice.Name}: {choice.Choice} ({choice.Confidence:P1})");
            break;
        case FoundryScoreAnswer score:
            Console.WriteLine($"{score.Name}: {score.Score:F2}");
            break;
        case FoundryRefusalAnswer refusal:
            Console.WriteLine($"{refusal.Name}: refused, requires application review");
            break;
    }
}
if (result.Usage is { } usage)
    Console.WriteLine($"Total tokens reported: {usage.TotalTokens}");
```

Question names must be unique, and each request must contain 1-400 questions.
Answers are matched by name, validated against the question, and returned in
request order even if the service reorders them. Choice retains every option
probability and the separate provider confidence. Score retains ordered
`Value`, `Label`, and `Probability` entries and computes an expected zero-based
level, not a percentage. Predicate is a probability in [0,1], not a Boolean.

Refusals are typed per-question answers. Do not cast every answer to its expected
success type without checking for `FoundryRefusalAnswer`. The shared
`ChooseAsync`, `ScoreAsync`, and `AssessAsync` methods instead throw
`DecisionException` on refusal, since their result types cannot represent it.
Missing usage or model metadata remains null. Token counts are not comparable
cost or computation across models.

For images, replace `FromText` with a user message:

```csharp
var input = FoundryDecisionInput.FromMessages([
    FoundryDecisionMessage.User([
        FoundryDecisionContentPart.Text("Inspect the attached product image."),
        FoundryDecisionContentPart.Image("https://<public-host>/product.png")
    ])
]);
```

Image parts accept HTTPS URLs or supported image data URLs. The service must
be able to fetch remote images without caller cookies or credentials. The
library enforces 128 images maximum; the service additionally enforces valid
image decoding and the 32 MB combined image limit. The client does not download
remote images to validate them.

The shared `IDecisionClient` remains text-only, one question per request, and
maps Assess to Predicate in OpenAI mode. Existing SystemOne consumers need no
configuration changes. See the
[step-by-step sample](../samples/12-FoundryDecision/README.md) and
[live validation scope](testing.md#live-foundry-test).

## Native Jev semantics

Reference: [official API](https://docs.typesafe.ai/api), [primitives](https://docs.typesafe.ai/primitives/choice), [models](https://docs.typesafe.ai/models).

## Typed questions

Create a `JevQuestionKey<TAnswer>` and use `request.WithQuestion(key, question)`. The association is checked at compile time when adding the question and at runtime when retrieving `response.GetAnswer(key)`. Duplicate identifiers are rejected. The raw dictionary constructor is available for dynamically assembled requests.

Requests/questions are immutable snapshots. `JsonElement` inputs are cloned so the original `JsonDocument` can be disposed. Use `JevJson.Text` for text, `JevJson.Parse` for JSON, or `JevJson.From(value, jsonTypeInfo)` for user types with explicit serialization metadata.

State must be a JSON string, object, or array. Questions are evaluated independently against the same state. IDs correlate answers; their ordering is not meaningful and they are not instructions to the model.

## Choice

`JevChoiceQuestion` takes 1-255 case-sensitive labels and descriptions. Labels and descriptions influence inference. The SDK never substitutes enum ordinals, changes casing, or automatically adds an `other` label.

`JevChoiceAnswer` preserves `Choice`, `Probabilities`, and server `Confidence`. Confidence is not the selected option's probability and is not guaranteed accuracy. Define an explicit fallback option and review policy when the labels are not exhaustive.

## Score

Provide 2-10 self-contained ordered levels. The response is a fractional expected zero-based position:

`score = sum(index * probability[index])`

Keep the full distribution: certainty at level 1 and equal probabilities at levels 0 and 2 both yield 1.0 but have different uncertainty. The library neither rounds scores nor interprets them as percentages. Probability and legend keys remain native string indices (`"0"`, `"1"`...). Legends can contain structured JSON.

## Noul

`JevNoulAnswer.Probability` represents the probability of a proposition, in [0,1]. It has no invented confidence or automatic boolean/severity conversion. Thresholds belong to the application and need calibration. True/false outcome criteria are optional.

## Models

As documented when this preview was created:

| Constant | ID | Behavior |
| --- | --- | --- |
| `JevModels.Version1_13_0` | `jev-1.13.0` | Pinned version |
| `JevModels.Latest` | `jev-latest` | Moving stable alias; SDK default |
| `JevModels.Preview` | `jev-preview` | Moving preview alias; may equal stable |

Any nonblank future string ID is accepted. `/v1/models` can list aliases without listing every valid pinned ID. Discovery is not a whitelist. The response's `Model` records what actually executed.

Samples default to the pinned model. Recalibrate thresholds after changing models. Context/rate limits are provider-controlled and can change; the SDK does not invent a character-to-token estimator.

## Native response fidelity

Full native JSON, including unknown fields, is retained in `RawRepresentation`. Required fields, answer discriminators, expected question IDs, distributions, score indices, and numeric ranges are checked. Missing usage stays nullable. Unsupported answer types cause an explicit `JevProtocolException` with native data, not an empty successful answer.

The SDK does not renormalize distributions or recompute server confidence.

## Provider-reported token usage

Starting in 0.6.3, `ChoiceDecision`, `ScoreDecision` and `AssessmentDecision`
expose optional `Usage` with nullable `InputTokens`, `OutputTokens`, `TotalTokens`,
`ReasoningTokens` and `CachedTokens`. Existing constructors remain available and
produce null usage. The counters describe the whole request, not an individual
label or rubric level.

```csharp
var result = await client.AssessAsync("The invoice is overdue.", "Payment is overdue.");
long? input = result.Usage?.InputTokens;
long? output = result.Usage?.OutputTokens;
long? reportedTotal = result.Usage?.TotalTokens;
```

| Provider | Availability |
| --- | --- |
| Foundry | Preserves the top-level System One `usage` object. Verified native responses can report input/output without `total_tokens`. |
| Ollama | Preserves native System One `usage` when the server/model reports it. Live local compatibility remains unverified. |
| Jev | The adapter carries native `JevUsage` into all three shared result types. Native results retain their model, request ID and raw JSON. Live Jev compatibility remains unverified. |

Missing or null usage produces null shared-result `Usage`. An empty usage object
produces an object with unknown counters. Missing or null counters stay null;
reported zero stays zero. Counts must be nonnegative 64-bit integers.
`total_tokens` is preserved exactly, even if different from input plus output.
No count is estimated, and no total is inferred. Reasoning and cached counts are
read from `reasoning_tokens` / `cached_tokens`, or from
`output_tokens_details.reasoning_tokens` / `input_tokens_details.cached_tokens`
when the direct count is absent. Unknown fields are ignored.

For cumulative usage, track each known counter separately and track requests with
unknown counters rather than silently treating them as zero. Token counts from
different models do not imply equivalent computation or cost; use the provider's
pricing and model-specific accounting.

No automatic OpenTelemetry instrumentation is added. Applications can record known
input/output counts as `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`
on their own activities without logging prompt text or credentials. Decision usage
must remain separately attributable from any subsequent generative chat call.

## Known contract questions

The HTTP reference and advanced/SDK docs disagree about some nullability. This preview accepts omitted/null instructions and structured/null descriptions as described by advanced docs, but requires non-null state as the HTTP reference does. Structured Score legends are preserved. Live tests include an explicit advanced-contract probe.

Exact identifier limits, Choice tie behavior, idempotency, remote cancellation/billing guarantees, and model retirement policy are not fully documented. These limitations must be reviewed during real-service validation before stable release.

Type-safe output is not semantic infallibility. The provider acknowledges that adversarial state can influence assessments; this SDK does not claim prompt-injection immunity.
