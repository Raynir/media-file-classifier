using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;

namespace MediaFileClassifier.Infrastructure.Tests.Hashing;

/// <summary>
/// Tests for <see cref="PerceptualHashIndex{TKey}"/>.
/// </summary>
public sealed class PerceptualHashIndexTests
{
    [Fact]
    public void FindWithin_ReturnsHashesWithinDistance_NearestFirst()
    {
        var index = new PerceptualHashIndex<string>();
        index.Add("far", 0xFFFF);
        index.Add("two", 0b0011);
        index.Add("same", 0);
        index.Add("one", 0b1000);

        var matches = index.FindWithin(0, 2);

        Assert.Equal(["same", "one", "two"], matches.Select(match => match.Key));
        Assert.Equal([0, 1, 2], matches.Select(match => match.Distance));
    }

    [Fact]
    public void FindWithin_SameKeyAddedTwice_CanMatchTwice()
    {
        var index = new PerceptualHashIndex<int>();
        index.Add(1, 0b01);
        index.Add(1, 0b10);

        Assert.Equal(2, index.FindWithin(0, 1).Count);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void FindWithin_EmptyIndex_ReturnsNothing()
    {
        Assert.Empty(new PerceptualHashIndex<int>().FindWithin(42, 64));
    }

    [Fact]
    public void FindWithin_100kHashes_TakesUnder100Milliseconds()
    {
        var random = new Random(339);
        var index = new PerceptualHashIndex<int>();
        for (var i = 0; i < 100_000; i++)
        {
            index.Add(i, (ulong)random.NextInt64() ^ ((ulong)random.Next() << 33));
        }

        var query = (ulong)random.NextInt64();
        index.FindWithin(query, PerceptualHash.SamePictureThreshold);

        var times = new List<TimeSpan>();
        for (var run = 0; run < 5; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            index.FindWithin(query, PerceptualHash.SamePictureThreshold);
            times.Add(stopwatch.Elapsed);
        }

        Assert.True(times.Min() < TimeSpan.FromMilliseconds(100), $"Fastest search took {times.Min().TotalMilliseconds} ms.");
    }
}
