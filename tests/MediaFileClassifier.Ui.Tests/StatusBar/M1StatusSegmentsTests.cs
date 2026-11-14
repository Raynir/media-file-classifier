using Avalonia.Threading;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Libraries.CountOfflineFolders;

namespace MediaFileClassifier.Ui.Tests.StatusBar;

/// <summary>
/// Tests for the M1 status bar segments (E13-S04-T02): the result count and the offline folder notice.
/// </summary>
public sealed class M1StatusSegmentsTests
{
    [Fact]
    public Task ResultCount_FollowsTheGrid_AndHidesWithoutALibrary() => TestApp.RunAsync(() =>
    {
        var source = new FakeResultSource(1204);
        var session = new FakeBrowserSession();
        var grid = new ThumbnailGridViewModel(
            source,
            new ThumbnailLoader(session, new FakeThumbnailCache(), new ThumbnailBitmapCache()),
            new FakeItemOpener(),
            new FakeJobSystem(),
            new FakeTileSizeSender(),
            new SelectionModel(() => source));
        var statusBar = new StatusBarViewModel([new ResultCountStatusSegment(() => grid, session, Localizer.Current)]);
        var segment = Assert.Single(statusBar.Left);
        Assert.Equal(ResultCountStatusSegment.SegmentId, segment.Id);
        Assert.Equal("0 items", segment.Text);

        grid.ActivateAsync().GetAwaiter().GetResult();
        Assert.Equal($"{Format.Number(1204)} items", segment.Text);
        Assert.True(segment.IsVisible);
        Assert.False(segment.IsClickable);

        source.Resize(1);
        grid.ActivateAsync().GetAwaiter().GetResult();
        Assert.Equal("1 item", segment.Text);

        session.SetCurrentAsync(null).GetAwaiter().GetResult();
        Assert.False(segment.IsVisible);
    });

    [Fact]
    public void ResultCount_LeavesTheSecondPlaceForTheSelectionCount()
    {
        Assert.Equal("browser.selection", ResultCountStatusSegment.SelectionSegmentId);
        Assert.Equal(1, ResultCountStatusSegment.SelectionOrder);
    }

    [Fact]
    public Task OfflineNotice_ShowsTheCount_AndRecountsWhenJobsStartOrStop() => TestApp.RunAsync(() =>
    {
        var sender = new FakeOfflineSender();
        var progress = new FakeJobProgress();
        var clock = new ManualTimeProvider();
        using var feed = new ProgressFeed(progress, clock, action => action());
        var statusBar = new StatusBarViewModel([new OfflineFoldersStatusSegment(sender, new FakeBrowserSession(), feed, Localizer.Current)]);
        var segment = Assert.Single(statusBar.Right);
        Assert.Equal(OfflineFoldersStatusSegment.SegmentId, segment.Id);
        Assert.False(segment.IsVisible);

        sender.Offline = 1;
        progress.Set(Running());
        Assert.True(segment.IsVisible);
        Assert.Equal("1 folder offline", segment.Text);
        Assert.False(segment.IsClickable);

        sender.Offline = 3;
        clock.Advance(ProgressFeed.Interval);
        progress.Set(ProgressSummary.Empty);
        clock.Advance(ProgressFeed.Interval);
        Assert.Equal("3 folders offline", segment.Text);

        sender.Offline = 0;
        progress.Set(Running());
        clock.Advance(ProgressFeed.Interval);
        Assert.False(segment.IsVisible);
    });

    [Fact]
    public Task OfflineNotice_RecountsWhenTheLibraryChanges() => TestApp.RunAsync(() =>
    {
        var sender = new FakeOfflineSender();
        var session = new FakeBrowserSession();
        var progress = new FakeJobProgress();
        using var feed = new ProgressFeed(progress, new ManualTimeProvider(), action => action());
        var statusBar = new StatusBarViewModel([new OfflineFoldersStatusSegment(sender, session, feed, Localizer.Current)]);
        var segment = Assert.Single(statusBar.Right);

        sender.Offline = 2;
        session.SetCurrentAsync(null).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.True(segment.IsVisible);
        Assert.Equal("2 folders offline", segment.Text);
    });

    private static ProgressSummary Running() => new(
        [new JobProgress(Id<ProcessingJob>.New(), "test", "Import", [new StageProgress("a", 10, 5, 0, false)], null)],
        null);

    /// <summary>
    /// Answers the offline folder count the test sets.
    /// </summary>
    private sealed class FakeOfflineSender : FakeSender
    {
        public int Offline { get; set; }

        protected override object? Handle(object request) => request switch
        {
            CountOfflineFoldersQuery => Offline,
            _ => throw new NotSupportedException(),
        };
    }
}
