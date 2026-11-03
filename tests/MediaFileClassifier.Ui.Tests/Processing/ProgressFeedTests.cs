using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.Tests.Shell;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// Tests for <see cref="ProgressFeed"/>: throttled, coalesced updates on the UI thread (E05-S02-T02).
/// </summary>
public sealed class ProgressFeedTests : IDisposable
{
    private readonly FakeJobProgress progress = new();
    private readonly ManualTimeProvider clock = new();
    private readonly List<Action> uiQueue = [];
    private readonly List<ProgressSummary> updates = [];
    private readonly ProgressFeed feed;

    public ProgressFeedTests()
    {
        feed = new ProgressFeed(progress, clock, uiQueue.Add);
        feed.Updated += (_, summary) => updates.Add(summary);
    }

    public void Dispose() => feed.Dispose();

    [Fact]
    public void FirstChange_GoesOutAtOnce_OnTheUiThread()
    {
        var summary = Summary(10);

        progress.Set(summary);

        Assert.Empty(updates);
        RunUiQueue();
        Assert.Same(summary, Assert.Single(updates));
        Assert.Same(summary, feed.Current);
    }

    [Fact]
    public void TenThousandChanges_AreCoalescedIntoAFewUpdates()
    {
        // A 10k-item job finishing within two seconds.
        for (var i = 0; i < 10_000; i++)
        {
            progress.Set(Summary(10_000 - i));
            if (i % 2_500 == 0)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
            }

            RunUiQueue();
        }

        clock.Advance(ProgressFeed.Interval);
        RunUiQueue();

        Assert.InRange(updates.Count, 2, 8);
        Assert.Equal(updates.Count, progress.Reads);
        Assert.Equal(1, updates[^1].PendingItems);
    }

    [Fact]
    public void ChangesWithinTheInterval_GoOutWhenItEnds()
    {
        progress.Set(Summary(10));
        RunUiQueue();
        progress.Set(Summary(9));
        progress.Set(Summary(8));
        RunUiQueue();

        clock.Advance(ProgressFeed.Interval - TimeSpan.FromMilliseconds(1));
        RunUiQueue();
        Assert.Single(updates);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        RunUiQueue();
        Assert.Equal([10, 8], updates.Select(update => update.PendingItems));
    }

    [Fact]
    public void WhileJobsAreActive_ItUpdatesEveryInterval_AndStopsWhenIdle()
    {
        progress.Set(Summary(10));
        RunUiQueue();
        clock.Advance(ProgressFeed.Interval);
        RunUiQueue();
        Assert.Equal(2, updates.Count);

        progress.Set(ProgressSummary.Empty);
        clock.Advance(ProgressFeed.Interval);
        RunUiQueue();
        clock.Advance(ProgressFeed.Interval);
        RunUiQueue();
        clock.Advance(ProgressFeed.Interval);
        RunUiQueue();

        Assert.Equal(3, updates.Count);
        Assert.Equal(0, updates[^1].ActiveJobCount);
    }

    private static ProgressSummary Summary(int pending) =>
        new([new JobProgress(Id<ProcessingJob>.New(), "import", "Import", [new StageProgress("stage", pending, 0, 0, false)], null)], null);

    private void RunUiQueue()
    {
        var actions = uiQueue.ToList();
        uiQueue.Clear();
        actions.ForEach(action => action());
    }
}
