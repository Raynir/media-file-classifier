using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Jobs;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for <see cref="JobProgressTracker"/>: counters, the current stage, growing totals and the estimate
/// (E05-S02-T01).
/// </summary>
public sealed class JobProgressTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock clock = new(Start);
    private readonly JobProgressTracker tracker;

    public JobProgressTrackerTests() => tracker = new JobProgressTracker(clock);

    [Fact]
    public void Counters_FollowTheWorkItems_AndTheCurrentStageIsTheOneFurthestBehind()
    {
        var job = NewJob();
        var fingerprints = Items(job, "fingerprint", 4);
        var thumbnails = Items(job, "thumbnail", 3);
        tracker.AddWork(job, [.. fingerprints, .. thumbnails]);

        Finish(job, fingerprints[0], WorkItemState.Completed);
        Finish(job, fingerprints[1], WorkItemState.Completed);
        Finish(job, fingerprints[2], WorkItemState.Failed);

        var progress = Assert.Single(tracker.Current.Jobs);
        Assert.Equal(["fingerprint", "thumbnail"], progress.Stages.Select(stage => stage.Stage));
        Assert.Equal(new StageProgress("fingerprint", 4, 2, 1, false), progress.Stages[0]);
        Assert.Equal("thumbnail", progress.CurrentStage!.Stage);
        Assert.Equal(4, progress.Pending);
        Assert.Equal(1, progress.Failed);
        Assert.False(progress.IsTotalGrowing);
    }

    [Fact]
    public void FinishedJob_IsForgotten()
    {
        var job = NewJob();
        var items = Items(job, "stage", 2);
        tracker.AddWork(job, items);

        Finish(job, items[0], WorkItemState.Completed);
        Finish(job with { State = ProcessingJobState.Completed }, items[1], WorkItemState.Completed);

        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void FinishedJob_ReportedBeforeAnEarlierItem_IsForgotten()
    {
        var job = NewJob();
        var items = Items(job, "stage", 2);
        tracker.AddWork(job, items);

        // Two items finished together: the database saw the first one while the job ran and the last one complete it,
        // but the threads reached the tracker in the other order.
        Finish(job with { State = ProcessingJobState.Completed }, items[1], WorkItemState.Completed);
        Finish(job with { State = ProcessingJobState.Running }, items[0], WorkItemState.Completed);

        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void FinishedJob_GettingNewWorkLater_IsKeptUntilTheNewWorkFinishes()
    {
        var job = NewJob();
        var first = Items(job, "stage", 2);
        tracker.AddWork(job, first);
        Finish(job with { State = ProcessingJobState.Completed }, first[1], WorkItemState.Completed);

        // New work reopens the job before the stale report of the earlier item arrives.
        var more = Items(job, "stage", 1);
        tracker.AddWork(job, more);
        Finish(job with { State = ProcessingJobState.Running }, first[0], WorkItemState.Completed);
        Assert.Equal(1, Assert.Single(tracker.Current.Jobs).Pending);

        Finish(job with { State = ProcessingJobState.Completed }, more[0], WorkItemState.Completed);
        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void FinishedJob_GettingNewWorkAtTheSameTime_IsKept()
    {
        var job = NewJob();
        var first = Items(job, "hash", 1);
        tracker.AddWork(job, first);
        tracker.AddWork(job, Items(job, "hash", 1));

        // The last stored item finished before the new one was written, so the job was Completed for a moment.
        Finish(job with { State = ProcessingJobState.Completed }, first[0], WorkItemState.Completed);

        Assert.Equal(1, Assert.Single(tracker.Current.Jobs).Pending);
    }

    [Fact]
    public void Estimate_IgnoresTheWaitBeforeTheFirstItemFinished()
    {
        var job = NewJob();
        tracker.AddWork(job, []);
        clock.Advance(TimeSpan.FromMinutes(10));
        RunSteadily(job, "stage", count: 100, perSecond: 10, left: 100);

        Assert.InRange(tracker.Current.Jobs[0].TimeRemaining!.Value.TotalSeconds, 9, 11);
    }

    [Fact]
    public void PendingWholeTargetWork_MeansTheTotalStillGrows_AndHidesTheEstimate()
    {
        var job = NewJob();
        var discover = WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, 0, null, Start);
        tracker.AddWork(job, [discover]);
        RunSteadily(job, "fingerprint", count: 40, perSecond: 2, left: 40);

        var growing = Assert.Single(tracker.Current.Jobs);
        Assert.True(growing.IsTotalGrowing);
        Assert.Equal("fingerprint", growing.CurrentStage!.Stage);
        Assert.Null(growing.TimeRemaining);
        Assert.Null(tracker.Current.TimeRemaining);

        Finish(job, discover, WorkItemState.Completed);

        Assert.False(tracker.Current.Jobs[0].IsTotalGrowing);
        Assert.NotNull(tracker.Current.TimeRemaining);
    }

    [Fact]
    public void OnlyWholeTargetWorkPending_IsTheCurrentStage()
    {
        var job = NewJob();
        tracker.AddWork(job, [WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, 0, null, Start)]);

        var progress = Assert.Single(tracker.Current.Jobs);
        Assert.Equal("discover", progress.CurrentStage!.Stage);
        Assert.True(progress.CurrentStage.IsWholeTarget);
    }

    [Theory]
    [InlineData(9, 10)]
    [InlineData(20, 4)]
    public void Estimate_NeedsTenFinishedItemsAndFiveSeconds(int finished, int seconds)
    {
        var job = NewJob();
        var items = Items(job, "stage", 100);
        tracker.AddWork(job, items);
        for (var i = 0; i < finished; i++)
        {
            Finish(job, items[i], WorkItemState.Completed);
        }

        clock.Advance(TimeSpan.FromSeconds(seconds));

        Assert.Null(tracker.Current.Jobs[0].TimeRemaining);
    }

    [Fact]
    public void Estimate_OfASteadyTenThousandItemJob_StabilisesWithinTenSeconds()
    {
        // A benchmark-like import: 10,000 items at about 50 items a second, in uneven bursts.
        var job = NewJob();
        var items = Items(job, "stage", 10_000);
        tracker.AddWork(job, items);
        var random = new Random(72);
        var estimates = new List<(TimeSpan Elapsed, TimeSpan Estimate, TimeSpan Actual)>();
        var done = 0;
        while (done < 3_000)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            for (var burst = random.Next(0, 11); burst > 0 && done < items.Count; burst--)
            {
                Finish(job, items[done++], WorkItemState.Completed);
            }

            if (tracker.Current.Jobs[0].TimeRemaining is { } estimate)
            {
                estimates.Add((clock.GetUtcNow() - Start, estimate, TimeSpan.FromSeconds((items.Count - done) / 50.0)));
            }
        }

        Assert.All(
            estimates.Where(sample => sample.Elapsed >= TimeSpan.FromSeconds(10)),
            sample => Assert.InRange(sample.Estimate / sample.Actual, 0.85, 1.15));
    }

    [Fact]
    public void Estimate_FollowsAChangeOfSpeedWithinTheWindow()
    {
        var job = NewJob();
        RunSteadily(job, "fast", count: 300, perSecond: 10);
        var slow = Items(job, "slow", 100);
        tracker.AddWork(job, slow);
        for (var i = 0; i < 70; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Finish(job, slow[i], WorkItemState.Completed);
        }

        // 30 left at 2 a second.
        Assert.InRange(tracker.Current.Jobs[0].TimeRemaining!.Value.TotalSeconds, 13, 17);
    }

    [Fact]
    public void CancelledWork_LeavesTheTotal()
    {
        var job = NewJob();
        var items = Items(job, "stage", 3);
        tracker.AddWork(job, items);

        Finish(job, items[0], WorkItemState.Cancelled);
        Finish(job, items[1], WorkItemState.Queued);

        Assert.Equal(new StageProgress("stage", 2, 0, 0, false), tracker.Current.Jobs[0].Stages[0]);
    }

    [Fact]
    public void RemoveWork_TakesBackTheItems_AndForgetsAnEmptyJob()
    {
        var job = NewJob();
        var items = Items(job, "stage", 3);
        tracker.AddWork(job, items);

        tracker.RemoveWork(job.Id, items);

        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void Summary_AddsUpTheJobs_AndRemoveJobForgetsOne()
    {
        var first = NewJob();
        var second = NewJob();
        tracker.AddWork(first, Items(first, "stage", 3));
        var items = Items(second, "stage", 5);
        tracker.AddWork(second, items);
        Finish(second, items[0], WorkItemState.Completed);

        var summary = tracker.Current;
        Assert.Equal(2, summary.ActiveJobCount);
        Assert.Equal(7, summary.PendingItems);
        Assert.Equal(8, summary.Total);
        Assert.Equal(1, summary.Finished);

        tracker.RemoveJob(first.Id);

        Assert.Equal(second.Id, Assert.Single(tracker.Current.Jobs).JobId);
    }

    [Fact]
    public void Reset_SeedsTheCounters_AndClearForgetsThem()
    {
        var job = NewJob();
        tracker.Reset(
            [job],
            [
                new WorkItemCount(job.Id, "fingerprint", WorkItemState.Completed, false, 7),
                new WorkItemCount(job.Id, "fingerprint", WorkItemState.Queued, false, 3),
                new WorkItemCount(job.Id, "discover", WorkItemState.Queued, true, 1),
                new WorkItemCount(Id<ProcessingJob>.New(), "other", WorkItemState.Queued, false, 9),
            ]);

        var progress = Assert.Single(tracker.Current.Jobs);
        Assert.Equal(["discover", "fingerprint"], progress.Stages.Select(stage => stage.Stage));
        Assert.Equal(new StageProgress("fingerprint", 10, 7, 0, false), progress.Stages[1]);
        Assert.True(progress.IsTotalGrowing);

        tracker.Clear();

        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void Changes_RaiseChanged()
    {
        var raised = 0;
        tracker.Changed += (_, _) => raised++;
        var job = NewJob();
        var items = Items(job, "stage", 2);

        tracker.AddWork(job, items);
        Finish(job, items[0], WorkItemState.Completed);
        tracker.RemoveJob(job.Id);

        Assert.Equal(3, raised);
    }

    private static ProcessingJob NewJob() =>
        new() { Id = Id<ProcessingJob>.New(), Type = "import", Description = "Import photos", CreatedAt = Start };

    private static List<WorkItem> Items(ProcessingJob job, string stage, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => WorkItem.Create(job.Id, Id<MediaItem>.New(), stage, ResourcePool.Cpu, 0, null, Start))];

    private void Finish(ProcessingJob job, WorkItem workItem, WorkItemState state) =>
        tracker.Finished(workItem.ChangeState(WorkItemState.Running, clock.GetUtcNow()).ChangeState(state, clock.GetUtcNow()), job);

    private void RunSteadily(ProcessingJob job, string stage, int count, double perSecond, int left = 0)
    {
        var items = Items(job, stage, count + left);
        tracker.AddWork(job, items);
        for (var i = 0; i < count; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1 / perSecond));
            Finish(job, items[i], WorkItemState.Completed);
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan time) => now += time;
    }
}
