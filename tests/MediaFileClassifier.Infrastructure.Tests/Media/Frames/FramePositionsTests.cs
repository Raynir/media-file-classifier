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
}
