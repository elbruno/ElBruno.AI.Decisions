using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;
using LocalDecisionAnswer = Microsoft.AI.Foundry.Local.DecisionAnswer;
using LocalDecisionQuestionKind = Microsoft.AI.Foundry.Local.DecisionQuestionKind;
using LocalDecisionRequest = Microsoft.AI.Foundry.Local.DecisionRequest;
using LocalDecisionResult = Microsoft.AI.Foundry.Local.DecisionResult;
using LocalDecisionUsage = Microsoft.AI.Foundry.Local.DecisionUsage;

namespace ElBruno.AI.Decisions.Foundry.Local.Tests;

public sealed class FoundryLocalDecisionClientTests
{
    [Fact]
    public async Task ChooseMapsStateInstructionsCriteriaAndProbabilities()
    {
        LocalDecisionRequest? request = null;
        var client = Create((value, _) =>
        {
            request = value;
            return Task.FromResult(Result(new LocalDecisionAnswer
            {
                Type = LocalDecisionQuestionKind.Choice,
                Choice = "support",
                Confidence = .8,
                Probabilities = new Dictionary<string, double> { ["support"] = .8, ["billing"] = .2 }
            }));
        });

        ChoiceDecision result = await client.ChooseAsync(
            "The invoice is wrong.",
            "Which team should handle this?",
            new Dictionary<string, string?> { ["support"] = "Technical issues", ["billing"] = "Payments" });

        Assert.Equal("The invoice is wrong.", request!.State!.Value.GetString());
        Assert.Equal("choice", request.Questions["decision"].Type);
        Assert.Equal("Which team should handle this?", request.Questions["decision"].Instructions!.Value.GetString());
        Assert.Equal("Technical issues", request.Questions["decision"].Criteria!.Value.GetProperty("support").GetString());
        Assert.Equal("support", result.Choice);
        Assert.Equal(.2, result.Probabilities["billing"]);
        Assert.Equal(.8, result.Confidence);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ScoreOrdersProbabilitiesByRubricIndex()
    {
        var client = Create((_, _) => Task.FromResult(Result(new LocalDecisionAnswer
        {
            Type = LocalDecisionQuestionKind.Score,
            Score = .25,
            Probabilities = new Dictionary<string, double> { ["1"] = .25, ["0"] = .75 }
        })));

        ScoreDecision result = await client.ScoreAsync("ticket", "Rate urgency.", ["low", "high"]);

        Assert.Equal(.25, result.Score);
        Assert.Equal([.75, .25], result.LevelProbabilities);
    }

    [Fact]
    public async Task RejectsMissingOrInvalidProbabilityDistributions()
    {
        var client = Create((_, _) => Task.FromResult(Result(new LocalDecisionAnswer
        {
            Type = LocalDecisionQuestionKind.Choice,
            Choice = "a",
            Probabilities = new Dictionary<string, double> { ["a"] = .7, ["b"] = .2 }
        })));

        await Assert.ThrowsAsync<DecisionException>(() => client.ChooseAsync(
            "input", "choose", new Dictionary<string, string?> { ["a"] = null, ["b"] = null }));
    }

    [Fact]
    public async Task RejectsAnswerTypeMismatchInsteadOfReturningDefault()
    {
        var client = Create((_, _) => Task.FromResult(Result(new LocalDecisionAnswer
        {
            Type = LocalDecisionQuestionKind.Choice,
            Choice = "a",
            Probabilities = new Dictionary<string, double> { ["a"] = 1 }
        })));

        await Assert.ThrowsAsync<DecisionException>(() => client.AssessAsync("input", "proposition"));
    }

    [Theory]
    [InlineData("noul,choice,score,structured-input", true)]
    [InlineData("candidate-ranking,structured-input,action-cache", false)]
    [InlineData("noul,choice", false)]
    [InlineData(null, false)]
    public void ResolverRequiresEveryDecisionCapability(string? capabilities, bool expected) =>
        Assert.Equal(expected, FoundryLocalDecisionModelResolver.SupportsDecisionCapabilities(capabilities));

    [Theory]
    [InlineData("""{"Name":"kev-0.8b-generic-cpu:2"}""")]
    [InlineData("""{"Alias":""}""")]
    public void PackageWithoutAliasOverrideCanUseDistinctRegistration(string json)
    {
        using JsonDocument metadata = JsonDocument.Parse(json);
        FoundryLocalDecisionModelResolver.EnsureCompatiblePackageAlias(metadata.RootElement, "elbruno-decisions-test");
    }

    [Theory]
    [InlineData("""{"Alias":"kev-0.8b"}""")]
    [InlineData("""{"Alias":null}""")]
    public void PackageAliasOverrideOrInvalidMetadataIsRejected(string json)
    {
        using JsonDocument metadata = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() =>
            FoundryLocalDecisionModelResolver.EnsureCompatiblePackageAlias(metadata.RootElement, "elbruno-decisions-test"));
    }

    [FoundryLocalSmokeFact]
    public async Task CachedKev08bRunsChoiceScoreAndNoulInference()
    {
        await FoundryLocalManager.CreateAsync(new Configuration
        {
            AppName = "foundry",
            ModelCacheDir = Environment.GetEnvironmentVariable("DECISIONS_FOUNDRY_LOCAL_CACHE")
        }, NullLogger.Instance);
        FoundryLocalManager manager = FoundryLocalManager.Instance;
        IModel? model = null;
        try
        {
            ICatalog localCatalog = await manager.GetCatalogAsync(CatalogType.Local);
            string? priorId = (await localCatalog.GetModelAsync("kev-0.8b"))?.Id;
            IModel[] resolved = await Task.WhenAll(
                FoundryLocalDecisionModelResolver.ResolveAsync("kev-0.8b"),
                FoundryLocalDecisionModelResolver.ResolveAsync("kev-0.8b"));
            model = resolved[0];
            Assert.Equal(resolved[1].Id, model.Id);
            Assert.NotEqual("kev-0.8b", model.Alias);
            Assert.StartsWith("elbruno-decisions-", model.Alias);
            Assert.Equal(model.Id, (await localCatalog.GetModelAsync(model.Alias))?.Id);
            Assert.Equal(priorId, (await localCatalog.GetModelAsync("kev-0.8b"))?.Id);
            var client = new FoundryLocalDecisionClient(model);

            AssessmentDecision assessment = await client.AssessAsync(
                "A customer cannot sign in and requests a password reset.",
                "Does this request concern account access?");
            ChoiceDecision choice = await client.ChooseAsync(
                "A customer cannot sign in and requests a password reset.",
                "Which team should handle this request?",
                new Dictionary<string, string?> { ["support"] = "Account support", ["billing"] = "Payments" });
            ScoreDecision score = await client.ScoreAsync(
                "A customer cannot sign in and requests a password reset.",
                "How urgent is this issue?",
                ["low", "high"]);

            Assert.InRange(assessment.Probability, 0, 1);
            Assert.Contains(choice.Choice, new[] { "support", "billing" });
            Assert.Equal(1, choice.Probabilities.Values.Sum(), precision: 6);
            Assert.InRange(score.Score, 0, 1);
            Assert.Equal(1, score.LevelProbabilities.Sum(), precision: 6);
        }
        finally
        {
            if (model is not null && await model.IsLoadedAsync())
                await model.UnloadAsync();
            manager.Dispose();
        }
    }

    private static FoundryLocalDecisionClient Create(
        Func<LocalDecisionRequest, CancellationToken, Task<LocalDecisionResult>> evaluate) => new(evaluate);

    private static LocalDecisionResult Result(LocalDecisionAnswer answer) => new()
    {
        Model = "kev-0.8b-generic-cpu:2",
        Answers = new Dictionary<string, LocalDecisionAnswer> { ["decision"] = answer },
        Usage = new LocalDecisionUsage()
    };
}

internal sealed class FoundryLocalSmokeFactAttribute : FactAttribute
{
    public FoundryLocalSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DECISIONS_RUN_FOUNDRY_LOCAL") != "1")
            Skip = "Set DECISIONS_RUN_FOUNDRY_LOCAL=1 to run cached Foundry Local model inference.";
    }
}
