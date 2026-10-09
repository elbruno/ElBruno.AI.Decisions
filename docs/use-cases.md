# Decision use cases

## Start with Foundry

Use Microsoft-Decision-1 for fixed-outcome tasks such as support routing,
intent classification, answer-quality evaluation, or relevance assessment.
Create a `FoundryDecisionClient` as shown in the
[main README](../README.md#1-foundry-microsoft-decision-1).
Use your deployment name and the resource endpoint, not the project portal URL.
See [the full runnable sample](../samples/12-FoundryDecision/README.md).

After creating `client`, the following shared API examples work with Foundry,
Ollama, or `JevDecisionClientAdapter`:

```csharp
var route = await client.ChooseAsync(
    "I was charged twice.", "Which team should handle this request?",
    new Dictionary<string, string?> { ["billing"] = "Charges and invoices", ["support"] = "Technical problems" });
Console.WriteLine($"{route.Choice}: confidence {route.Confidence:P1}");
foreach (var option in route.Probabilities)
    Console.WriteLine($"{option.Key}: {option.Value:P1}");
```

Use descriptions that make the categories distinct. Include a fallback category
when inputs may fall outside your supported options. Set human-review thresholds
from labeled evaluation data, not from an arbitrary confidence percentage.

```csharp
var quality = await client.ScoreAsync(
    "Reset your password from the account page.",
    "How helpful is this response to someone who forgot their password?",
    ["Does not address the problem", "Partially helpful", "Clear actionable solution"]);
Console.WriteLine($"Expected level: {quality.Score:F2} of 2");
```

Rubrics are ordered from lowest to highest. The score is a fractional expected
zero-based level, not a rounded grade. `LevelProbabilities` retains uncertainty.
Score supports 2-10 levels.

```csharp
var relevance = await client.AssessAsync(
    "This passage explains dependency injection in .NET.",
    "The passage is relevant to a question about .NET dependency injection.");
Console.WriteLine($"Relevant: {relevance.Probability:P1}");
```

Assess is a probability of a proposition, not a boolean, a severity grade, or
a guarantee of safety. Foundry expresses this as a native Noul question.

## Run the same tasks locally with Ollama

Use the [Ollama sample](../samples/11-OllamaLocal/README.md) to run all three
operations with Ollama v0.35.0+ and `ollama pull nimble`.
The client calls `/v1/systemone` with native Choice, Score and Noul questions,
not `/api/chat`. General chat and cloud models are not supported.
Choose limits are model-specific (typically 2-26, at most 255 in the API).
The shared Score API supports 2-10 levels; text requests must fit within 64 KiB.

Confidence is `1 - H(p) / ln(N)`, a measure of distribution concentration,
not proof of calibrated correctness. Models can be unreliable for safety assessments.
Evaluate your chosen model on the actual workload; do not use the injection
example as an automatic security gate.

## Example responses

Foundry and Ollama use the same named System One answer shape. The SDK names
its single question `decision`. These are illustrative values, not recorded
live outputs. Model names, usage, probabilities, and confidence vary.

Foundry Choice (response excerpt):

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

Ollama Choice:

```json
{
  "model": "nimble",
  "answers": {
    "decision": {
      "type": "choice",
      "choice": "billing",
      "probabilities": { "billing": 0.9, "support": 0.1 },
      "confidence": 0.531
    }
  },
  "usage": { "input_tokens": 174, "output_tokens": 1 }
}
```

`ChoiceDecision.Confidence` preserves the provider confidence separately from
`Probabilities["billing"]`. Do not equate the two values.

Score answer (either provider, Ollama also returns the rubric legend):

```json
{
  "type": "score",
  "score": 1.7,
  "legend": { "0": "Not helpful", "1": "Partly helpful", "2": "Very helpful" },
  "probabilities": { "0": 0.1, "1": 0.1, "2": 0.8 },
  "confidence": 0.4183
}
```

The SDK exposes `Score = 1.7` and `LevelProbabilities = [0.1, 0.1, 0.8]`.
Assess answer: `{"type":"noul","noul":0.98}`, exposed as
`AssessmentDecision.Probability = 0.98`, not a Boolean.

See [Ollama's decision guide](https://docs.ollama.com/capabilities/decision)
and [API reference](https://docs.ollama.com/api/systemone).
The native Ollama correction requires NuGet `0.6.1` or later, not `0.6.0`.

## Native Jev and advanced scenarios

Use Jev when you need multiple typed questions in a single request, structured
JSON evidence, or native model discovery. Start with
[Choice](../samples/01-HelloChoice/README.md),
[Score and Noul](../samples/02-ScoreAndNoul/README.md),
[parallel questions](../samples/03-ParallelDecisions/README.md), and
[structured state](../samples/04-StructuredState/README.md).

For application composition, see
[DI](../samples/06-DependencyInjection/README.md),
[agent function tools](../samples/07-MEAI-Tools/README.md),
[chat routing](../samples/08-MEAI-Routing/README.md),
[input/output assessments](../samples/09-MEAI-Assessments/README.md), and
[RAG reranking](../samples/10-RagReranking/README.md).
These are native Jev examples; the shared `DecisionRoutingChatClient` can
instead route using any `IDecisionClient` plus caller-supplied chat clients.

## Configuration, failures, and validation

See [configuration](configuration.md) for secrets and authentication,
[testing](testing.md) for opt-in live calls, and
[release validation](releasing.md) for exact-package consumer checks.
Foundry's documented route is `/providers/microsoft/v1/systemone`.
API-key authentication takes precedence over Azure CLI authentication.

Never hardcode secrets. An offline sample exercises synthetic transport only.
Smoke tests verify basic compatibility, not calibration, accuracy, latency under
load, or production readiness. Decisions about people in consequential domains
must not rely on this model as the sole decision-maker.
