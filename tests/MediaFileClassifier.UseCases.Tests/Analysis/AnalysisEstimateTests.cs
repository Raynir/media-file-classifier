using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

namespace MediaFileClassifier.UseCases.Tests.Analysis;

/// <summary>
/// Tests for <see cref="GetAnalysisEstimateQueryHandler"/> (E06-S06-T03): the estimate is the image count plus the
/// sampled video frames, over the benchmark's image rate.
/// </summary>
public sealed class AnalysisEstimateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The RTX 4070 benchmark: CPU 8.7/s, DirectML standard tier 29/s, compact tier 36.5/s.
    [Theory]
    [InlineData(8.7, 10_000, 1149.4)]
    [InlineData(29, 10_000, 344.8)]
    [InlineData(36.5, 10_000, 274.0)]
    public async Task Estimate_FollowsTheBenchmarkRate_ForImages(double rate, int images, double seconds)
    {
        var handler = new GetAnalysisEstimateQueryHandler(new FixedHints(rate));

        var estimate = await handler.Handle(new GetAnalysisEstimateQuery(images, 0), Token);

        Assert.Equal(seconds, estimate!.Value.TotalSeconds, 1);
    }

    [Fact]
    public async Task Estimate_CountsAVideoAsItsFrames()
    {
        var handler = new GetAnalysisEstimateQueryHandler(new FixedHints(29));
        var frames = 20 * FrameSamplingOptions.Default.FrameCount(TimeSpan.FromMinutes(2));

        var estimate = await handler.Handle(new GetAnalysisEstimateQuery(290, frames), Token);

        Assert.Equal(20 * 16, frames);
        Assert.Equal((290 + frames) / 29.0, estimate!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 4)]
    [InlineData(40, 8)]
    [InlineData(600, 16)]
    public void FrameCount_IsOnePerInterval_BetweenTheMinimumAndMaximum(int seconds, int expected) =>
        Assert.Equal(expected, FrameSamplingOptions.Default.FrameCount(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public async Task Estimate_IsEmpty_WithoutARate(double? rate)
    {
        var handler = new GetAnalysisEstimateQueryHandler(new FixedHints(rate));

        Assert.Null(await handler.Handle(new GetAnalysisEstimateQuery(1000, 0), Token));
        Assert.Null(await new GetAnalysisEstimateQueryHandler().Handle(new GetAnalysisEstimateQuery(1000, 0), Token));
    }

    private sealed class FixedHints(double? rate) : IThroughputHints
    {
        public double? ItemsPerSecond(string jobType) => rate;
    }
}
