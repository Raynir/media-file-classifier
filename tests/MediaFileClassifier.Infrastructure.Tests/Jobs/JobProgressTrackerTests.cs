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
    public void RetriedWork_IsCountedAsRetrying_NotAsFailed_UntilItFinishes()
    {
        var job = NewJob();
        var items = Items(job, "describe", 3);
        tracker.AddWork(job, items);
        WorkItem Running(WorkItem item) => item.ChangeState(WorkItemState.Running, Start);

        tracker.Finished(Running(items[0]).ScheduleRetry(Start.AddSeconds(2), ErrorCategory.Transient, "down", countsAsRetry: true, Start), job);
        tracker.Finished(Running(items[1]).Park(ErrorCategory.Transient, "down", Start), job);

        tracker.Finished(Running(items[2]).ScheduleRetry(Start.AddMinutes(1), ErrorCategory.Configuration, "model missing", countsAsRetry: false, Start), job);

        var progress = Assert.Single(tracker.Current.Jobs);
        Assert.Equal((2, 0, 3), (progress.Retrying, progress.Failed, progress.Pending));
        Assert.Equal(2, tracker.Current.Retrying);

        Finish(job, items[0], WorkItemState.Completed);
        Finish(job, items[1], WorkItemState.Failed);

        progress = Assert.Single(tracker.Current.Jobs);
        Assert.Equal((0, 1), (progress.Retrying, progress.Failed));
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
    public void AThousandItemJob_ReportsFinishedExactlyOnce_WithItsFinalCounters()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "fingerprint", 1_000);
        tracker.AddWork(job, items);

        for (var i = 0; i < items.Count - 1; i++)
        {
            Finish(job, items[i], i % 100 == 0 ? WorkItemState.Failed : WorkItemState.Completed);
        }

        Assert.Empty(finished);

        Finish(job with { State = ProcessingJobState.Completed }, items[^1], WorkItemState.Completed);

        var args = Assert.Single(finished);
        Assert.Equal(ProcessingJobState.Completed, args.State);
        Assert.Equal(job.Id, args.Progress.JobId);
        Assert.Equal(new StageProgress("fingerprint", 1_000, 990, 10, false), Assert.Single(args.Progress.Stages));
    }

    [Fact]
    public void AJobReportedFinishedBeforeAnEarlierItem_ReportsFinishedOnce_WhenTheLastItemArrives()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "stage", 2);
        tracker.AddWork(job, items);

        Finish(job with { State = ProcessingJobState.Failed }, items[1], WorkItemState.Failed);
        Assert.Empty(finished);
        Finish(job with { State = ProcessingJobState.Running }, items[0], WorkItemState.Failed);

        Assert.Equal(ProcessingJobState.Failed, Assert.Single(finished).State);
    }

    [Fact]
    public void AFailingChangedHandler_OrJobFinishedHandler_DoesNotLoseTheEventForOthers()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, _) => throw new InvalidOperationException("first listener fails");
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "stage", 1);
        tracker.AddWork(job, items);
        var failOnChange = false;
        tracker.Changed += (_, _) =>
        {
            if (failOnChange)
            {
                throw new InvalidOperationException("status bar fails");
            }
        };

        failOnChange = true;
        Assert.Throws<InvalidOperationException>(() => Finish(job with { State = ProcessingJobState.Completed }, items[0], WorkItemState.Completed));

        Assert.Single(finished);
    }

    [Fact]
    public void ACancelledJob_AndAClearedTracker_ReportNothing()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var cancelled = NewJob();
        tracker.AddWork(cancelled, Items(cancelled, "stage", 2));
        tracker.RemoveJob(cancelled.Id);
        var other = NewJob();
        tracker.AddWork(other, Items(other, "stage", 2));
        tracker.Clear();

        Assert.Empty(finished);
    }

    [Fact]
    public void WorkAddedToAFinishedJob_MakesItReportFinishedAgain()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var first = Items(job, "stage", 1);
        tracker.AddWork(job, first);
        Finish(job with { State = ProcessingJobState.Completed }, first[0], WorkItemState.Completed);

        var retry = Items(job, "stage", 1);
        tracker.AddWork(job, retry);
        Finish(job with { State = ProcessingJobState.Completed }, retry[0], WorkItemState.Completed);

        Assert.Equal(2, finished.Count);
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
    public void KnownRate_GivesAnEstimateBeforeAnyItemFinished_AndTheMeasuredRateTakesOverLater()
    {
        var seeded = new JobProgressTracker(clock, new FixedHints("import", 5));
        var job = NewJob();
        var items = Items(job, "stage", 100);
        seeded.AddWork(job, items);

        // Nothing finished: 100 items at the benchmark's 5 per second.
        Assert.Equal(TimeSpan.FromSeconds(20), seeded.Current.Jobs[0].TimeRemaining);
        Assert.Equal(TimeSpan.FromSeconds(20), seeded.Current.TimeRemaining);

        // Measured: 20 items in 5 s (4 per second), 80 left, so 20 s as well, from the measurement.
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            seeded.Finished(items[i].ChangeState(WorkItemState.Running, clock.GetUtcNow()).ChangeState(WorkItemState.Completed, clock.GetUtcNow()), job);
        }

        Assert.InRange(seeded.Current.Jobs[0].TimeRemaining!.Value.TotalSeconds, 15, 25);
    }

    [Fact]
    public void KnownRate_IsOnlyUsedForItsOwnJobType()
    {
        var seeded = new JobProgressTracker(clock, new FixedHints("other-type", 5));
        var job = NewJob();
        seeded.AddWork(job, Items(job, "stage", 10));

        Assert.Null(seeded.Current.Jobs[0].TimeRemaining);
        Assert.Null(seeded.Current.TimeRemaining);
    }

    private sealed class FixedHints(string jobType, double rate) : IThroughputHints
    {
        public double? ItemsPerSecond(string type) => type == jobType ? rate : null;
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
    public void EndAddWork_WithNothingStored_TakesBackTheItems_AndForgetsAnEmptyJob()
    {
        var job = NewJob();
        var items = Items(job, "stage", 3);
        var addition = tracker.AddWork(job, items);

        tracker.EndAddWork(addition, items);

        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void FailedEnqueue_ToAJobReportedFinished_RollsBackTheCounts_AndReportsFinishedOnceWhenTheRestCompletes()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "stage", 2);
        AddStored(job, items);
        Finish(job with { State = ProcessingJobState.Completed }, items[1], WorkItemState.Completed);
        var before = tracker.Current.Jobs[0].Stages[0];

        // The write of more work fails, so the job stays finished in the database.
        var more = Items(job, "stage", 3);
        var addition = tracker.AddWork(job, more);
        tracker.EndAddWork(addition, more);

        Assert.Equal(before, tracker.Current.Jobs[0].Stages[0]);
        Assert.Empty(finished);

        // The earlier item's report, read while the job still ran, arrives last.
        Finish(job with { State = ProcessingJobState.Running }, items[0], WorkItemState.Completed);

        var args = Assert.Single(finished);
        Assert.Equal(ProcessingJobState.Completed, args.State);
        Assert.Equal(new StageProgress("stage", 2, 2, 0, false), Assert.Single(args.Progress.Stages));
        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void FailedEnqueue_WhileTheRestOfTheJobFinished_ReportsFinishedOnceWhenTheWriteEnds()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "stage", 1);
        AddStored(job, items);
        var more = Items(job, "stage", 2);
        var addition = tracker.AddWork(job, more);

        // The last stored item finishes the job while the new work is still being written.
        Finish(job with { State = ProcessingJobState.Failed }, items[0], WorkItemState.Failed);
        Assert.Empty(finished);

        tracker.EndAddWork(addition, more);
        tracker.EndAddWork(addition, more);

        Assert.Equal(ProcessingJobState.Failed, Assert.Single(finished).State);
        Assert.Same(ProgressSummary.Empty, tracker.Current);
    }

    [Fact]
    public void PartlyStoredEnqueue_KeepsTheStoredBatch_AndTheJobFinishesOnceItRuns()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var items = Items(job, "stage", 4);
        var addition = tracker.AddWork(job, items);

        // The first batch was stored, then the write failed.
        tracker.EndAddWork(addition, items[2..]);

        Assert.Equal(new StageProgress("stage", 2, 0, 0, false), Assert.Single(tracker.Current.Jobs).Stages[0]);
        Finish(job, items[0], WorkItemState.Completed);
        Finish(job with { State = ProcessingJobState.Completed }, items[1], WorkItemState.Completed);

        Assert.Equal(new StageProgress("stage", 2, 2, 0, false), Assert.Single(Assert.Single(finished).Progress.Stages));
    }

    [Fact]
    public void FailedEnqueue_AlongsideAStoredOne_LeavesTheJobOpenUntilTheStoredWorkFinishes()
    {
        var finished = new List<JobFinishedEventArgs>();
        tracker.JobFinished += (_, e) => finished.Add(e);
        var job = NewJob();
        var first = Items(job, "stage", 2);
        AddStored(job, first);
        Finish(job with { State = ProcessingJobState.Completed }, first[1], WorkItemState.Completed);
        var failing = Items(job, "stage", 2);
        var stored = Items(job, "stage", 1);
        var failed = tracker.AddWork(job, failing);
        var succeeded = tracker.AddWork(job, stored);

        tracker.EndAddWork(succeeded, []);
        tracker.EndAddWork(failed, failing);

        // The stored work reopened the job, so the earlier finished report doesn't count.
        Assert.Equal(2, Assert.Single(tracker.Current.Jobs).Pending);
        Finish(job with { State = ProcessingJobState.Running }, first[0], WorkItemState.Completed);
        Assert.Empty(finished);
        Finish(job with { State = ProcessingJobState.Completed }, stored[0], WorkItemState.Completed);
        Assert.Single(finished);
    }

    [Fact]
    public void EndAddWork_AfterTheCountersWereReset_ChangesNothing_AndQueuedNeverGoesNegative()
    {
        var job = NewJob();
        var items = Items(job, "stage", 2);
        var addition = tracker.AddWork(job, items);
        tracker.Reset([job], [new WorkItemCount(job.Id, "stage", WorkItemState.Queued, false, 1)]);

        tracker.EndAddWork(addition, items);
        Finish(job, items[0], WorkItemState.Completed);
        Finish(job, items[1], WorkItemState.Completed);

        // The second item was never counted after the reset: it adds to the total instead of going below zero.
        Assert.Equal(new StageProgress("stage", 2, 2, 0, false), Assert.Single(tracker.Current.Jobs).Stages[0]);
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

    // Work stored without a hitch, as the scheduler adds it.
    private void AddStored(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems) =>
        tracker.EndAddWork(tracker.AddWork(job, workItems), []);

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
