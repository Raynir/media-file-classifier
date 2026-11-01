using MediaFileClassifier.Evaluation.Metrics;

namespace MediaFileClassifier.Evaluation.Tests.Metrics;

/// <summary>
/// Tests for <see cref="RetrievalMetrics"/>.
/// </summary>
public sealed class RetrievalMetricsTests
{
    private static readonly HashSet<string> Relevant = ["a", "b", "c", "d"];

    [Fact]
    public void RecallAt_CountsRelevantItemsInTheCutoff()
    {
        string[] ranked = ["x", "a", "y", "b", "z", "c"];

        Assert.Equal(0.5, RetrievalMetrics.RecallAt(2, ranked, Relevant));
        Assert.Equal(0.75, RetrievalMetrics.RecallAt(10, ranked, Relevant));
    }

    [Fact]
    public void RecallAt_IsCappedAtTheCutoff()
    {
        string[] ranked = ["a", "b", "x", "c"];

        // Four relevant items but only three results counted: two of three is the best share possible.
        Assert.Equal(2.0 / 3, RetrievalMetrics.RecallAt(3, ranked, Relevant), 10);
        Assert.Equal(1.0, RetrievalMetrics.RecallAt(2, ranked, Relevant));
    }

    [Fact]
    public void RecallAt_CountsARepeatedItemOnce()
    {
        string[] ranked = ["a", "a", "a", "b"];

        Assert.Equal(2.0 / 3, RetrievalMetrics.RecallAt(3, ranked, Relevant), 10);
    }

    [Fact]
    public void RecallAt_RejectsAQueryWithoutRelevantItems()
    {
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.RecallAt(10, ["a"], new HashSet<string>()));
    }

    [Theory]
    [InlineData(new[] { "a", "x" }, 1.0)]
    [InlineData(new[] { "x", "y", "b" }, 1.0 / 3)]
    [InlineData(new[] { "x", "x", "x", "c" }, 0.5)]
    [InlineData(new[] { "x", "y" }, 0.0)]
    [InlineData(new string[0], 0.0)]
    public void ReciprocalRank_IsOneOverTheFirstRelevantRank(string[] ranked, double expected)
    {
        Assert.Equal(expected, RetrievalMetrics.ReciprocalRank(ranked, Relevant), 10);
    }

    [Fact]
    public void FirstRelevantRank_IsNullWhenNothingRelevantIsReturned()
    {
        Assert.Null(RetrievalMetrics.FirstRelevantRank(["x", "y"], Relevant));
        Assert.Equal(2, RetrievalMetrics.FirstRelevantRank(["x", "d"], Relevant));
    }
}
