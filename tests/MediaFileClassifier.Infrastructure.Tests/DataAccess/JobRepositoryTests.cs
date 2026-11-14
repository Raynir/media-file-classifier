using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="JobRepository"/> against a real library database (E05-S01-T01).
/// </summary>
public sealed class JobRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now => LibraryTestBed.Start;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task AddJobAsync_StoresJobAndWorkItems()
    {
        var (job, items) = NewJob(3);

        await bed.Library.Jobs.AddJobAsync(job, items, Token);

        Assert.Equivalent(job, await bed.Library.Jobs.GetJobAsync(job.Id, Token));
        Assert.Equal(items.Select(item => item.Id), (await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token)).Select(item => item.Id));
        Assert.Equal(3, await bed.Library.Jobs.CountPendingAsync(Token));
    }

    [Fact]
    public async Task GetQueuedAsync_OrdersByPriorityThenAge_AndFiltersPoolAndExclusions()
    {
        var job = NewJobRecord();
        var low = WorkItem.Create(job.Id, null, "hash", ResourcePool.Io, (int)JobPriority.FullHashing, null, Now);
        var high = WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, (int)JobPriority.Import, null, Now.AddSeconds(5));
        var older = WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, (int)JobPriority.Import, null, Now.AddSeconds(1));
        var otherPool = WorkItem.Create(job.Id, null, "thumbnail", ResourcePool.Cpu, (int)JobPriority.UserAction, null, Now);
        await bed.Library.Jobs.AddJobAsync(job, [low, high, older, otherPool], Token);

        var queued = await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 10, [], null, null, Token);
        var excluding = await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 10, [older.Id], null, null, Token);

        Assert.Equal([older.Id, high.Id, low.Id], queued.Select(item => item.Id));
        Assert.Equal([high.Id, low.Id], excluding.Select(item => item.Id));
        Assert.Single(await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 1, [], null, null, Token));
    }

    [Fact]
    public async Task GetQueuedForItemsAsync_FindsWorkForThoseItems()
    {
        var media = await AddMediaItemsAsync(2);
        var job = NewJobRecord();
        var first = WorkItem.Create(job.Id, media[0].Id, "thumbnail", ResourcePool.Cpu, (int)JobPriority.Import, null, Now);
        var second = WorkItem.Create(job.Id, media[1].Id, "thumbnail", ResourcePool.Cpu, (int)JobPriority.Import, null, Now);
        await bed.Library.Jobs.AddJobAsync(job, [first, second], Token);

        var found = await bed.Library.Jobs.GetQueuedForItemsAsync([media[1].Id], Token);

        Assert.Equal(second.Id, Assert.Single(found).Id);
    }

    [Fact]
    public async Task StartThenFinish_RunsJobToCompletion()
    {
        var (job, items) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);

        var running = await bed.Library.Jobs.StartAsync(items[0], Now.AddSeconds(1), Token);
        Assert.Equal(ProcessingJobState.Running, (await bed.Library.Jobs.GetJobAsync(job.Id, Token))!.State);
        var afterFirst = await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Completed, Now.AddSeconds(2)), Now.AddSeconds(2), Token);
        var second = await bed.Library.Jobs.StartAsync(items[1], Now.AddSeconds(3), Token);
        var afterSecond = await bed.Library.Jobs.FinishAsync(second!.ChangeState(WorkItemState.Completed, Now.AddSeconds(4)), Now.AddSeconds(4), Token);

        Assert.Equal(1, running.AttemptCount);
        Assert.Equal(ProcessingJobState.Running, afterFirst.State);
        Assert.Equal(ProcessingJobState.Completed, afterSecond.State);
        Assert.Equal(Now.AddSeconds(1), afterSecond.StartedAt);
        Assert.Equal(Now.AddSeconds(4), afterSecond.FinishedAt);
        Assert.Equal(0, await bed.Library.Jobs.CountPendingAsync(Token));
    }

    [Fact]
    public async Task FinishAsync_SomeItemsFailed_CompletesTheJob_AndKeepsTheErrors()
    {
        var (job, items) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);

        var first = await bed.Library.Jobs.StartAsync(items[0], Now, Token);
        await bed.Library.Jobs.FinishAsync(first!.ChangeState(WorkItemState.Failed, Now, "corrupt"), Now, Token);
        var second = await bed.Library.Jobs.StartAsync(items[1], Now, Token);
        var finished = await bed.Library.Jobs.FinishAsync(second!.ChangeState(WorkItemState.Completed, Now), Now, Token);

        Assert.Equal(ProcessingJobState.Completed, finished.State);
        Assert.Equal("corrupt", (await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token))[0].LastError);
    }

    [Fact]
    public async Task FinishAsync_EveryItemFailed_FailsTheJob()
    {
        var (job, items) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        ProcessingJob? finished = null;

        foreach (var item in items)
        {
            var running = await bed.Library.Jobs.StartAsync(item, Now, Token);
            finished = await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Failed, Now, "corrupt"), Now, Token);
        }

        Assert.Equal(ProcessingJobState.Failed, finished!.State);
    }

    [Fact]
    public async Task StartAsync_ItemNoLongerQueued_ReturnsNull()
    {
        var (job, items) = NewJob(1);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        await bed.Library.Jobs.CancelJobAsync(job.Id, Now, Token);

        Assert.Null(await bed.Library.Jobs.StartAsync(items[0], Now, Token));
    }

    [Fact]
    public async Task CancelJobAsync_CancelsQueuedItems_AndKeepsFinishedOnes()
    {
        var (job, items) = NewJob(3);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        var done = await bed.Library.Jobs.StartAsync(items[0], Now, Token);
        await bed.Library.Jobs.FinishAsync(done!.ChangeState(WorkItemState.Completed, Now), Now, Token);

        Assert.True(await bed.Library.Jobs.CancelJobAsync(job.Id, Now.AddSeconds(1), Token));
        Assert.False(await bed.Library.Jobs.CancelJobAsync(job.Id, Now.AddSeconds(1), Token));

        var stored = await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token);
        Assert.Equal([WorkItemState.Completed, WorkItemState.Cancelled, WorkItemState.Cancelled], stored.Select(item => item.State));
        Assert.Equal(ProcessingJobState.Cancelled, (await bed.Library.Jobs.GetJobAsync(job.Id, Token))!.State);
        Assert.Empty(await bed.Library.Jobs.GetUnfinishedJobsAsync(Token));
    }

    [Fact]
    public async Task AddWorkItemsAsync_ReopensFinishedJob_ButNotCancelledOne()
    {
        var (job, items) = NewJob(1);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        var running = await bed.Library.Jobs.StartAsync(items[0], Now, Token);
        await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Completed, Now), Now, Token);

        var next = WorkItem.Create(job.Id, null, "next", ResourcePool.Cpu, (int)JobPriority.Import, null, Now);
        Assert.True(await bed.Library.Jobs.AddWorkItemsAsync(job.Id, [next], Now, Token));
        Assert.Equal(ProcessingJobState.Queued, (await bed.Library.Jobs.GetJobAsync(job.Id, Token))!.State);

        await bed.Library.Jobs.CancelJobAsync(job.Id, Now, Token);
        Assert.False(await bed.Library.Jobs.AddWorkItemsAsync(job.Id, [WorkItem.Create(job.Id, null, "next", ResourcePool.Cpu, 0, null, Now)], Now, Token));
        Assert.False(await bed.Library.Jobs.AddWorkItemsAsync(Id<ProcessingJob>.New(), [WorkItem.Create(job.Id, null, "x", ResourcePool.Cpu, 0, null, Now)], Now, Token));
    }

    [Fact]
    public async Task RequeueRunningAsync_PutsInterruptedWorkBackInTheQueue()
    {
        var (job, items) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        await bed.Library.Jobs.StartAsync(items[0], Now, Token);

        // As after a crash: the library is closed with the work item still running.
        await bed.ReopenAsync();
        var requeued = await bed.Library.Jobs.RequeueRunningAsync(Now.AddMinutes(1), Token);

        Assert.Equal(1, requeued);
        Assert.All(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token), item => Assert.Equal(WorkItemState.Queued, item.State));
        Assert.Equal(ProcessingJobState.Queued, Assert.Single(await bed.Library.Jobs.GetUnfinishedJobsAsync(Token)).State);
    }

    [Fact]
    public async Task DeletingMediaItem_DeletesItsWorkItems()
    {
        var media = await AddMediaItemsAsync(1);
        var job = NewJobRecord();
        await bed.Library.Jobs.AddJobAsync(job, [WorkItem.Create(job.Id, media[0].Id, "thumbnail", ResourcePool.Cpu, 0, null, Now)], Token);

        await bed.Library.Items.DeleteAsync(media[0].Id, Token);

        Assert.Empty(await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token));
    }

    [Fact]
    public async Task GetRecentJobsAsync_ListsFinishedJobsNewestFirst_UpToTheLimit()
    {
        var finished = new List<ProcessingJob>();
        for (var i = 0; i < 3; i++)
        {
            var (job, items) = NewJob(1);
            await bed.Library.Jobs.AddJobAsync(job, items, Token);
            var running = await bed.Library.Jobs.StartAsync(items[0], Now.AddMinutes(i), Token);
            finished.Add(await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Completed, Now.AddMinutes(i)), Now.AddMinutes(i), Token));
        }

        var (unfinished, unfinishedItems) = NewJob(1);
        await bed.Library.Jobs.AddJobAsync(unfinished, unfinishedItems, Token);

        var recent = await bed.Library.Jobs.GetRecentJobsAsync(2, Token);

        Assert.Equal([finished[2].Id, finished[1].Id], recent.Select(job => job.Id));
    }

    [Fact]
    public async Task CountWorkAsync_CountsByStageAndState_ForTheGivenJobsOnly()
    {
        var (job, items) = NewJob(3);
        var (other, otherItems) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        await bed.Library.Jobs.AddJobAsync(other, otherItems, Token);
        var running = await bed.Library.Jobs.StartAsync(items[0], Now, Token);
        await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Completed, Now), Now, Token);

        var counts = await bed.Library.Jobs.CountWorkAsync([job.Id], Token);

        Assert.All(counts, count => Assert.Equal(job.Id, count.JobId));
        Assert.Equal(1, counts.Single(count => count.State == WorkItemState.Completed).Count);
        Assert.Equal(2, counts.Single(count => count.State == WorkItemState.Queued).Count);
    }

    [Fact]
    public async Task CountFailuresAsync_CountsFailuresWhoseItemIsStillInError_OrHasNoItem()
    {
        var media = await AddMediaItemsAsync(3);
        var job = NewJobRecord();
        var stillBroken = WorkItem.Create(job.Id, media[0].Id, "fingerprint", ResourcePool.Io, 0, null, Now);
        var retried = WorkItem.Create(job.Id, media[1].Id, "fingerprint", ResourcePool.Io, 0, null, Now.AddSeconds(1));
        var wholeTarget = WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, 0, null, Now.AddSeconds(2));
        var fine = WorkItem.Create(job.Id, media[2].Id, "fingerprint", ResourcePool.Io, 0, null, Now.AddSeconds(3));
        await bed.Library.Jobs.AddJobAsync(job, [stillBroken, retried, wholeTarget, fine], Token);
        foreach (var failing in new[] { stillBroken, retried, wholeTarget })
        {
            var running = await bed.Library.Jobs.StartAsync(failing, Now, Token);
            await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Failed, Now, "broken"), Now, Token);
        }

        // Two items are in error; the retried one went back to pending when the user retried it.
        media[0].MarkFailed(new MediaItemError("fingerprint", null, false), "broken", Now);
        await bed.Library.Items.UpdateAsync(media[0], Token);

        var failures = await bed.Library.Jobs.CountFailuresAsync([job.Id], Token);

        Assert.Equal(2, failures[job.Id]);
    }

    [Fact]
    public async Task GetFailedWorkItemsAsync_ListsTheFailuresThatNeedAttention_WithTheirMessage()
    {
        var media = await AddMediaItemsAsync(2);
        var job = NewJobRecord();
        var stillBroken = WorkItem.Create(job.Id, media[0].Id, "fingerprint", ResourcePool.Io, 0, null, Now);
        var retried = WorkItem.Create(job.Id, media[1].Id, "fingerprint", ResourcePool.Io, 0, null, Now.AddSeconds(1));
        var wholeTarget = WorkItem.Create(job.Id, null, "discover", ResourcePool.Io, 0, null, Now.AddSeconds(2));
        await bed.Library.Jobs.AddJobAsync(job, [stillBroken, retried, wholeTarget], Token);
        foreach (var failing in new[] { stillBroken, retried, wholeTarget })
        {
            var running = await bed.Library.Jobs.StartAsync(failing, Now, Token);
            await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Failed, Now, "broken " + failing.Stage), Now, Token);
        }

        media[0].MarkFailed(new MediaItemError("fingerprint", null, false), "broken", Now);
        await bed.Library.Items.UpdateAsync(media[0], Token);

        var failed = await bed.Library.Jobs.GetFailedWorkItemsAsync(job.Id, Token);

        Assert.Equal([stillBroken.Id, wholeTarget.Id], failed.Select(item => item.Id));
        Assert.Equal("broken fingerprint", failed[0].LastError);
        Assert.Empty(await bed.Library.Jobs.GetFailedWorkItemsAsync(Id<ProcessingJob>.New(), Token));
    }

    [Fact]
    public async Task PruneFinishedJobsAsync_KeepsTheJobsThatFinishedLast_AndDeletesTheRestWithTheirWorkItems()
    {
        var jobs = new List<ProcessingJob>();
        for (var i = 0; i < 5; i++)
        {
            jobs.Add(await AddFinishedJobAsync(Now.AddMinutes(i), ProcessingJobState.Completed));
        }

        var deleted = await bed.Library.Jobs.PruneFinishedJobsAsync(2, DateTimeOffset.MinValue, Token);

        Assert.Equal(3, deleted);
        Assert.All(jobs.Take(3), job => Assert.Null(bed.Library.Jobs.GetJobAsync(job.Id, Token).Result));
        Assert.All(jobs.Skip(3), job => Assert.NotNull(bed.Library.Jobs.GetJobAsync(job.Id, Token).Result));
        Assert.All(jobs.Take(3), job => Assert.Empty(bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token).Result));
        Assert.All(jobs.Skip(3), job => Assert.NotEmpty(bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token).Result));
    }

    [Fact]
    public async Task PruneFinishedJobsAsync_DeletesJobsThatFinishedBeforeTheLimit_EvenWithinTheKeptCount()
    {
        var old = await AddFinishedJobAsync(Now.AddDays(-40), ProcessingJobState.Failed);
        var recent = await AddFinishedJobAsync(Now.AddDays(-1), ProcessingJobState.Completed);

        var deleted = await bed.Library.Jobs.PruneFinishedJobsAsync(50, Now.AddDays(-30), Token);

        Assert.Equal(1, deleted);
        Assert.Null(await bed.Library.Jobs.GetJobAsync(old.Id, Token));
        Assert.NotNull(await bed.Library.Jobs.GetJobAsync(recent.Id, Token));
    }

    [Fact]
    public async Task PruneFinishedJobsAsync_NeverDeletesUnfinishedJobs()
    {
        var (queued, queuedItems) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(queued, queuedItems, Token);
        var (running, runningItems) = NewJob(1);
        await bed.Library.Jobs.AddJobAsync(running, runningItems, Token);
        await bed.Library.Jobs.StartAsync(runningItems[0], Now, Token);

        var deleted = await bed.Library.Jobs.PruneFinishedJobsAsync(0, Now.AddYears(10), Token);

        Assert.Equal(0, deleted);
        Assert.NotNull(await bed.Library.Jobs.GetJobAsync(queued.Id, Token));
        Assert.NotNull(await bed.Library.Jobs.GetJobAsync(running.Id, Token));
        Assert.Equal(2, (await bed.Library.Jobs.GetWorkItemsAsync(queued.Id, Token)).Count);
    }

    [Fact]
    public async Task PruneFinishedJobsAsync_KeepsAFinishedJobThatNewWorkWokeUp()
    {
        var woken = await AddFinishedJobAsync(Now.AddDays(-40), ProcessingJobState.Completed);
        var asleep = await AddFinishedJobAsync(Now.AddDays(-40), ProcessingJobState.Completed);
        var more = WorkItem.Create(woken.Id, null, "stage", ResourcePool.Cpu, 0, null, Now);
        Assert.True(await bed.Library.Jobs.AddWorkItemsAsync(woken.Id, [more], Now, Token));

        var deleted = await bed.Library.Jobs.PruneFinishedJobsAsync(0, Now.AddDays(-30), Token);

        Assert.Equal(1, deleted);
        Assert.Equal(ProcessingJobState.Queued, (await bed.Library.Jobs.GetJobAsync(woken.Id, Token))!.State);
        Assert.Contains(more.Id, (await bed.Library.Jobs.GetWorkItemsAsync(woken.Id, Token)).Select(item => item.Id));
        Assert.Null(await bed.Library.Jobs.GetJobAsync(asleep.Id, Token));
    }

    private async Task<ProcessingJob> AddFinishedJobAsync(DateTimeOffset finishedAt, ProcessingJobState state)
    {
        var (job, items) = NewJob(1);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        var running = await bed.Library.Jobs.StartAsync(items[0], finishedAt, Token);
        var workState = state == ProcessingJobState.Completed ? WorkItemState.Completed : WorkItemState.Failed;
        await bed.Library.Jobs.FinishAsync(running!.ChangeState(workState, finishedAt, workState == WorkItemState.Failed ? "broken" : null), finishedAt, Token);
        var finished = await bed.Library.Jobs.GetJobAsync(job.Id, Token);
        Assert.True(ProcessingJobStateTransitions.IsFinished(finished!.State));
        return finished;
    }

    private static ProcessingJob NewJobRecord() =>
        new() { Id = Id<ProcessingJob>.New(), Type = "test", Description = "Test job", Priority = (int)JobPriority.Import, CreatedAt = Now };

    private static (ProcessingJob Job, WorkItem[] Items) NewJob(int itemCount)
    {
        var job = NewJobRecord();
        var items = Enumerable.Range(0, itemCount)
            .Select(i => WorkItem.Create(job.Id, null, "stage", ResourcePool.Cpu, job.Priority, null, Now.AddMilliseconds(i)))
            .ToArray();
        return (job, items);
    }

    private async Task<IReadOnlyList<MediaItem>> AddMediaItemsAsync(int count)
    {
        var items = Enumerable.Range(0, count).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        return items;
    }
}
