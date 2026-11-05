using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Media.Frames;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Frames;

/// <summary>
/// Tests for <see cref="FrameSelector{T}"/> (E06-S02-T02).
/// </summary>
public sealed class FrameSelectorTests
{
    private const ulong Base = 0;

    [Fact]
    public void Offer_NearDuplicateOfThePreviousKeptFrame_IsDropped()
    {
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default);

        selector.Offer("a", Base, isBlank: false, meanLuma: 100);
        selector.Offer("a+8 bits", Bits(8), isBlank: false, meanLuma: 100);
        selector.Offer("a+9 bits", Bits(9), isBlank: false, meanLuma: 100);

        Assert.Equal(["a", "a+9 bits"], selector.Kept);
        Assert.Equal(3, selector.Offered);
    }

    [Fact]
    public void Offer_SlowDrift_IsComparedWithTheLastKeptFrameNotTheLastOffered()
    {
        // Each frame is 5 bits from the one before, but the third is 10 bits from the last kept one.
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default);

        selector.Offer("a", Base, isBlank: false, meanLuma: 100);
        selector.Offer("b", Bits(5), isBlank: false, meanLuma: 100);
        selector.Offer("c", Bits(10), isBlank: false, meanLuma: 100);

        Assert.Equal(["a", "c"], selector.Kept);
    }

    [Fact]
    public void Offer_BlankFrames_AreDropped()
    {
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default);

        selector.Offer("black", Base, isBlank: true, meanLuma: 2);
        selector.Offer("scene", Bits(30), isBlank: false, meanLuma: 120);

        Assert.Equal(["scene"], selector.Kept);
    }

    [Fact]
    public void Kept_EveryFrameBlank_KeepsTheBrightest()
    {
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default);

        selector.Offer("dark", Base, isBlank: true, meanLuma: 3);
        selector.Offer("grey card", Bits(20), isBlank: true, meanLuma: 128);
        selector.Offer("darker", Bits(40), isBlank: true, meanLuma: 1);

        Assert.Equal(["grey card"], selector.Kept);
    }

    [Fact]
    public void Offer_BlankFramesKept_WhenTheOptionIsOff()
    {
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default with { DropBlankFrames = false });

        selector.Offer("black", Base, isBlank: true, meanLuma: 2);
        selector.Offer("scene", Bits(30), isBlank: false, meanLuma: 120);

        Assert.Equal(["black", "scene"], selector.Kept);
    }

    [Fact]
    public void Offer_ThresholdZero_DropsOnlyIdenticalHashes()
    {
        var selector = new FrameSelector<string>(FrameSamplingOptions.Default with { DuplicateThreshold = 0 });

        selector.Offer("a", Base, isBlank: false, meanLuma: 100);
        selector.Offer("same", Base, isBlank: false, meanLuma: 100);
        selector.Offer("one bit", Bits(1), isBlank: false, meanLuma: 100);

        Assert.Equal(["a", "one bit"], selector.Kept);
    }

    [Fact]
    public void Kept_NothingOffered_IsEmpty()
    {
        Assert.Empty(new FrameSelector<string>(FrameSamplingOptions.Default).Kept);
    }

    /// <summary>
    /// Gets a hash with the lowest <paramref name="count"/> bits set, that many bits from <see cref="Base"/>.
    /// </summary>
    private static ulong Bits(int count) => count == 64 ? ulong.MaxValue : (1UL << count) - 1;
}
