using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Media.Frames;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Frames;

/// <summary>
/// Tests for <see cref="FramePositions"/> (E06-S02-T02, T03).
/// </summary>
public sealed class FramePositionsTests
{
    private static readonly FrameSamplingOptions Options = FrameSamplingOptions.Default;

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 4)]
    [InlineData(20, 4)]
    [InlineData(24.9, 4)]
    [InlineData(50, 10)]
    [InlineData(80, 16)]
    [InlineData(7200, 16)]
    public void Count_IsOnePerFiveSecondsBetweenFourAndSixteen(double seconds, int expected)
    {
        Assert.Equal(expected, FramePositions.Count(TimeSpan.FromSeconds(seconds), Options));
    }

    [Fact]
    public void Count_FollowsTheOptions()
    {
        var options = new FrameSamplingOptions { MinFrames = 2, MaxFrames = 6, Interval = TimeSpan.FromSeconds(10) };

        Assert.Equal(2, FramePositions.Count(TimeSpan.FromSeconds(5), options));
        Assert.Equal(3, FramePositions.Count(TimeSpan.FromSeconds(35), options));
        Assert.Equal(6, FramePositions.Count(TimeSpan.FromMinutes(5), options));
    }

    [Fact]
    public void ForVideo_TakesTheMidpointsOfEqualSlices()
    {
        Assert.Equal(
            [TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(7.5), TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(17.5)],
            FramePositions.ForVideo(TimeSpan.FromSeconds(20), Options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void ForVideo_UnknownDuration_TakesTheStart(double? seconds)
    {
        Assert.Equal([TimeSpan.Zero], FramePositions.ForVideo(seconds is { } value ? TimeSpan.FromSeconds(value) : null, Options));
    }

    [Fact]
    public void ForAnimation_VariableDelays_PicksTheFrameShownAtEachPosition()
    {
        // Like gif-variable-delays.gif: 1 s, 1 s, ten of 100 ms, 1 s. Positions 0.5, 1.5, 2.5 and 3.5 s.
        var delays = Delays([1000, 1000, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 1000]);

        Assert.Equal([0, 1, 7, 12], FramePositions.ForAnimation(delays, Options));
    }

    [Fact]
    public void ForAnimation_LongFrameCoveringSeveralPositions_IsPickedOnce()
    {
        // 3.5 s, positions at 0.44, 1.31, 2.19 and 3.06 s: the first three all fall in frame 0.
        var delays = Delays([3000, 100, 100, 100, 100, 100]);

        Assert.Equal([0, 1], FramePositions.ForAnimation(delays, Options));
    }

    [Fact]
    public void ForAnimation_FewerFramesThanTheMinimum_UsesEveryFrame()
    {
        Assert.Equal([0, 1, 2], FramePositions.ForAnimation(Delays([200, 200, 200]), Options));
    }

    [Fact]
    public void ForAnimation_NoDelays_SpreadsByFrameIndex()
    {
        Assert.Equal([1, 3, 5, 7], FramePositions.ForAnimation(Delays([0, 0, 0, 0, 0, 0, 0, 0]), Options));
    }

    [Fact]
    public void ForAnimation_NoFrameInfo_TakesTheFirstFrame()
    {
        Assert.Equal([0], FramePositions.ForAnimation([], Options));
    }

    [Fact]
    public void StartOf_IsTheSumOfTheEarlierDelays()
    {
        var delays = Delays([1000, 1000, 100, 100, 100, 100, 100]);

        Assert.Equal(TimeSpan.Zero, FramePositions.StartOf(delays, 0));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), FramePositions.StartOf(delays, 7));
    }

    private static TimeSpan[] Delays(int[] milliseconds) => [.. milliseconds.Select(value => TimeSpan.FromMilliseconds(value))];
}
