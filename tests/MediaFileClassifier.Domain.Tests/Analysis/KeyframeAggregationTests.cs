using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Analysis;

/// <summary>
/// Tests for <see cref="KeyframeAggregation"/> with synthetic per-frame results (E06-S02-T04).
/// </summary>
public sealed class KeyframeAggregationTests
{
    [Fact]
    public void MergeTags_WeighsByHowOftenATagAppears()
    {
        // 4 frames: "dog" in all, "beach" in 2, "frisbee" in 1 (25%, the threshold), and none in the last frame.
        var frames = new[]
        {
            Tags(("dog", 0.9), ("beach", 0.8), ("frisbee", 0.6)),
            Tags(("dog", 0.7), ("beach", 0.6)),
            Tags(("dog", 0.8)),
            Tags(("dog", 0.6)),
        };

        var merged = KeyframeAggregation.MergeTags(frames);

        Assert.Equal(["dog", "beach", "frisbee"], merged.Select(tag => tag.Name.Value));
        Assert.Equal(0.75, merged[0].Confidence, 6);
        Assert.Equal(0.35, merged[1].Confidence, 6);
        Assert.Equal(0.15, merged[2].Confidence, 6);
        Assert.Equal([4, 2, 1], merged.Select(tag => tag.FrameCount));
    }

    [Fact]
    public void MergeTags_TagBelowTheShare_IsDropped()
    {
        // 16 frames; "cat" appears in 3 (under 25%), "car" in 4.
        var frames = Enumerable.Range(0, 16).Select(index => index switch
        {
            < 3 => Tags(("cat", 0.9), ("car", 0.5)),
            3 => Tags(("car", 0.5)),
            _ => Tags(),
        }).ToArray();

        var merged = KeyframeAggregation.MergeTags(frames);

        Assert.Equal("car", Assert.Single(merged).Name.Value);
    }

    [Fact]
    public void MergeTags_SameTagTwiceInAFrame_CountsOnceWithTheHigherConfidence()
    {
        var frames = new[] { Tags(("Beach", 0.4), ("beach", 0.8)), Tags() };

        var tag = Assert.Single(KeyframeAggregation.MergeTags(frames));

        Assert.Equal(1, tag.FrameCount);
        Assert.Equal(0.4, tag.Confidence, 6);
    }

    [Fact]
    public void MergeTags_CustomShare_IsApplied()
    {
        var frames = new[] { Tags(("dog", 1)), Tags(("dog", 1), ("cat", 1)), Tags(("dog", 1)), Tags(("dog", 1), ("cat", 1)) };

        Assert.Equal(["dog", "cat"], KeyframeAggregation.MergeTags(frames, minShare: 0.5).Select(tag => tag.Name.Value));
        Assert.Equal(["dog"], KeyframeAggregation.MergeTags(frames, minShare: 0.75).Select(tag => tag.Name.Value));
    }

    [Theory]
    [InlineData(0.3, 10, 3)]
    [InlineData(0.1, 30, 3)]
    [InlineData(0.7, 10, 7)]
    public void MergeTags_TagExactlyOnTheShare_IsKept(double share, int frameCount, int framesWithTag)
    {
        var frames = Enumerable.Range(0, frameCount).Select(index => index < framesWithTag ? Tags(("dog", 1)) : Tags()).ToArray();

        Assert.Single(KeyframeAggregation.MergeTags(frames, share));
        Assert.Empty(KeyframeAggregation.MergeTags(frames[1..], share));
    }

    [Fact]
    public void MergeTags_NoFrames_IsEmpty()
    {
        Assert.Empty(KeyframeAggregation.MergeTags([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void MergeTags_InvalidShare_Throws(double share)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyframeAggregation.MergeTags([Tags()], share));
    }

    [Fact]
    public void FrameTag_ConfidenceOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTag(TagName.From("dog"), 1.5));
    }

    [Fact]
    public void MergeTextLines_DropsRepeatsAndBlankLines_KeepingFirstSeenOrder()
    {
        string?[] frames =
        [
            "SALE  50%\r\nShop now",
            null,
            "  SALE 50%  \n\nOpen daily",
            "Shop now\nСкидка",
            "sale 50%",
        ];

        Assert.Equal(["SALE 50%", "Shop now", "Open daily", "Скидка", "sale 50%"], KeyframeAggregation.MergeTextLines(frames));
    }

    [Fact]
    public void MergeTextLines_NoText_IsEmpty()
    {
        Assert.Empty(KeyframeAggregation.MergeTextLines([null, " ", string.Empty]));
    }

    [Fact]
    public void MeanEmbedding_IsTheNormalisedMeanOfNormalisedVectors()
    {
        // Normalising first means the long second vector doesn't outweigh the first.
        ReadOnlyMemory<float>[] frames = [new[] { 1f, 0f }, new[] { 0f, 10f }];

        var mean = KeyframeAggregation.MeanEmbedding(frames);

        var half = MathF.Sqrt(0.5f);
        Assert.Equal(half, mean[0], 5);
        Assert.Equal(half, mean[1], 5);
    }

    [Fact]
    public void MeanEmbedding_OneFrame_IsThatFrameNormalised()
    {
        Assert.Equal([0.6f, 0.8f], KeyframeAggregation.MeanEmbedding([new[] { 3f, 4f }]));
    }

    [Fact]
    public void MeanEmbedding_HasUnitLength()
    {
        var random = new Random(84);
        var frames = Enumerable.Range(0, 16)
            .Select(_ => (ReadOnlyMemory<float>)Enumerable.Range(0, 768).Select(_ => (float)random.NextDouble() - 0.5f).ToArray())
            .ToArray();

        var mean = KeyframeAggregation.MeanEmbedding(frames);

        Assert.Equal(1, Math.Sqrt(mean.Sum(value => (double)value * value)), 5);
    }

    [Fact]
    public void MeanEmbedding_FramesThatCancelOut_FallsBackToTheFirstFrame()
    {
        Assert.Equal([1f, 0f], KeyframeAggregation.MeanEmbedding([new[] { 2f, 0f }, new[] { -1f, 0f }]));
    }

    [Fact]
    public void MeanEmbedding_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => KeyframeAggregation.MeanEmbedding([]));
        Assert.Throws<ArgumentException>(() => KeyframeAggregation.MeanEmbedding([new[] { 1f, 0f }, new[] { 1f }]));
        Assert.Throws<ArgumentException>(() => KeyframeAggregation.MeanEmbedding([new[] { 0f, 0f }]));
        Assert.Throws<ArgumentException>(() => KeyframeAggregation.MeanEmbedding([new[] { float.NaN, 0f }]));
    }

    private static FrameTag[] Tags(params (string Name, double Confidence)[] tags) =>
        [.. tags.Select(tag => new FrameTag(TagName.From(tag.Name), tag.Confidence))];
}
