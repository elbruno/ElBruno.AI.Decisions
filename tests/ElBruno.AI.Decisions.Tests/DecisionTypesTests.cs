namespace ElBruno.AI.Decisions.Tests;

public sealed class DecisionTypesTests
{
    [Fact]
    public void ExistingConstructorsRemainAvailableWithUnknownUsage()
    {
        Assert.Null(new ChoiceDecision("a", new Dictionary<string, double> { ["a"] = 1 }).Usage);
        Assert.Null(new ScoreDecision([.7, .3]).Usage);
        Assert.Null(new AssessmentDecision(.8).Usage);
        Assert.NotNull(typeof(ChoiceDecision).GetConstructor([typeof(string), typeof(IReadOnlyDictionary<string, double>), typeof(double?)]));
        Assert.NotNull(typeof(ScoreDecision).GetConstructor([typeof(IReadOnlyList<double>)]));
        Assert.NotNull(typeof(AssessmentDecision).GetConstructor([typeof(double)]));
    }

    [Fact]
    public void UsageIsPreservedWithoutComputingATotal()
    {
        var usage = new DecisionUsage(12, 3);
        Assert.Same(usage, new ChoiceDecision("a", new Dictionary<string, double> { ["a"] = 1 }, null, usage).Usage);
        Assert.Same(usage, new ScoreDecision([.7, .3], usage).Usage);
        Assert.Same(usage, new AssessmentDecision(.8, usage).Usage);
        Assert.Null(usage.TotalTokens);
    }

    [Fact]
    public void UsageRejectsEveryNegativeCounter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionUsage(inputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionUsage(outputTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionUsage(totalTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionUsage(reasoningTokens: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionUsage(cachedTokens: -1));
    }

    [Fact]
    public void ChoiceDefaultsConfidenceToSelectedProbability()
    {
        var d = new ChoiceDecision("a", new Dictionary<string, double> { ["a"] = .6, ["b"] = .4 });
        Assert.Equal(.6, d.Confidence);
    }

    [Fact]
    public void ChoiceRejectsLabelOutsideDistribution() =>
        Assert.Throws<ArgumentException>(() => new ChoiceDecision("c", new Dictionary<string, double> { ["a"] = 1 }));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void AssessmentRejectsInvalidProbability(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentDecision(value));

    [Fact]
    public void ScoreIsExpectedLevelPosition()
    {
        var d = new ScoreDecision([.25, .25, .5]);
        Assert.Equal(1.25, d.Score, 6);
    }

    [Fact]
    public void ScoreRequiresTwoLevels() => Assert.Throws<ArgumentException>(() => new ScoreDecision([1]));

    [Fact]
    public void ValidationEnforcesBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DecisionRequestValidation.Choice("t", "i", new Dictionary<string, string?> { ["a"] = null }));
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionRequestValidation.Score("t", "i", ["a"]));
        Assert.Throws<ArgumentException>(() => DecisionRequestValidation.Assess(" ", "p"));
    }
}
