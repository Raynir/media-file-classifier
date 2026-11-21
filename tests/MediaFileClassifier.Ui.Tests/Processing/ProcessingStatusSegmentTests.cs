using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Shell;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// Tests for the processing indicator (E05-S02-T03) and its text.
/// </summary>
public sealed class ProcessingStatusSegmentTests
{
    private readonly ProgressText text = new(Localizer.Current);

    [Fact]
    public void Segment_IsVisibleOnlyWhileAJobIsActive()
    {
        var progress = new FakeJobProgress();
        using var feed = new ProgressFeed(progress, new ManualTimeProvider(), action => action());
        var statusBar = new StatusBarViewModel([new ProcessingStatusSegment(feed, Localizer.Current)]);
        var segment = Assert.Single(statusBar.Right);
        Assert.Equal(ProcessingStatusSegment.SegmentId, segment.Id);
        Assert.False(segment.IsVisible);

        progress.Set(Summary(Job("Import photos", null, new StageProgress("import.fingerprint", 340, 118, 2, false))));

        Assert.True(segment.IsVisible);
        Assert.Equal("Fingerprinting: 120 of 340", segment.Text);
        Assert.Equal("Import photos — Fingerprinting: 120 of 340, 2 failed", segment.ToolTip);
        Assert.False(segment.IsClickable);
    }

    [Fact]
    public void Segment_ShowsWhileProcessingWaitsForResume_AndHidesAfterwards()
    {
        var progress = new FakeJobProgress();
        var jobs = new PausedJobSystem();
        var clock = new ManualTimeProvider();
        using var feed = new ProgressFeed(progress, clock, action => action());
        var statusBar = new StatusBarViewModel([new ProcessingStatusSegment(feed, Localizer.Current, jobs: jobs)]);
        var segment = Assert.Single(statusBar.Right);

        // A paused start still has its unfinished jobs in the counters.
        progress.Set(Summary(Job("Analyze 20 items", null, new StageProgress("custom", 20, 0, 0, false))));
        Assert.True(segment.IsVisible);
        Assert.Equal("Processing paused", segment.Text);

        jobs.Resume();
        progress.Set(ProgressSummary.Empty);
        clock.Advance(ProgressFeed.Interval);
        Assert.False(segment.IsVisible);
    }

    [Fact]
    public void Details_SayHowManyAreRetrying_BeforeAnyFailure()
    {
        var job = Job("Analyze photos", null, new StageProgress("custom", 10, 4, 0, false)) with { Retrying = 3 };

        Assert.Equal("Analyze photos — custom: 4 of 10, 3 retrying", text.Details(Summary(job)));
    }

    [Fact]
    public void Summary_ShowsTheTimeLeft_AndTheJobDescriptionForAnUnnamedStage()
    {
        var summary = Summary(Job("Full hashing", TimeSpan.FromMinutes(11.5), new StageProgress("custom", 900, 40, 0, false)));

        Assert.Equal("Full hashing: 40 of 900 · 12 min left", text.Summary(summary));
        Assert.Equal("Full hashing — custom: 40 of 900", text.Details(summary));
    }

    [Fact]
    public void Summary_OfAGrowingTotal_SaysSoFar()
    {
        var summary = Summary(Job(
            "Import photos",
            null,
            new StageProgress("import.discover", 1, 0, 0, true),
            new StageProgress("import.fingerprint", 300, 240, 0, false)));

        Assert.Equal("Fingerprinting: 240 of 300+ so far", text.Summary(summary));
    }

    [Fact]
    public void Summary_OfWholeTargetWork_NamesTheStage()
    {
        var summary = Summary(Job("Import photos", null, new StageProgress("import.discover", 1, 0, 0, true)));

        Assert.Equal("Finding files…", text.Summary(summary));
    }

    [Fact]
    public void Summary_OfSeveralJobs_CountsThem()
    {
        var summary = new ProgressSummary(
            [Job("Import", null, new StageProgress("a", 10, 5, 0, false)), Job("Hash", null, new StageProgress("b", 20, 0, 0, false))],
            TimeSpan.FromSeconds(30));

        Assert.Equal("2 jobs · 25 items left · less than a minute left", text.Summary(summary));
        Assert.Equal(2, text.Details(summary)!.Split(Environment.NewLine).Length);
        Assert.Null(text.Summary(ProgressSummary.Empty));
    }

    [Theory]
    [InlineData(59, "less than a minute")]
    [InlineData(61, "2 min")]
    [InlineData(3600, "1 h 0 min")]
    [InlineData(4_870, "1 h 22 min")]
    public void TimeLeft_RoundsUpToMinutes(int seconds, string expected) =>
        Assert.Equal(expected, text.TimeLeft(TimeSpan.FromSeconds(seconds)));

    private static ProgressSummary Summary(JobProgress job) => new([job], job.TimeRemaining);

    private static JobProgress Job(string description, TimeSpan? remaining, params StageProgress[] stages) =>
        new(Id<ProcessingJob>.New(), "test", description, stages, remaining);
}
