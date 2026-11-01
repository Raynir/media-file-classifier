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

        var queued = await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 10, [], Token);
        var excluding = await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 10, [older.Id], Token);

        Assert.Equal([older.Id, high.Id, low.Id], queued.Select(item => item.Id));
        Assert.Equal([high.Id, low.Id], excluding.Select(item => item.Id));
        Assert.Single(await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Io, 1, [], Token));
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
    public async Task FinishAsync_LastItemAfterAFailure_FailsTheJob()
    {
        var (job, items) = NewJob(2);
        await bed.Library.Jobs.AddJobAsync(job, items, Token);

        var first = await bed.Library.Jobs.StartAsync(items[0], Now, Token);
        await bed.Library.Jobs.FinishAsync(first!.ChangeState(WorkItemState.Failed, Now, "corrupt"), Now, Token);
        var second = await bed.Library.Jobs.StartAsync(items[1], Now, Token);
        var finished = await bed.Library.Jobs.FinishAsync(second!.ChangeState(WorkItemState.Completed, Now), Now, Token);

        Assert.Equal(ProcessingJobState.Failed, finished.State);
        Assert.Equal("corrupt", (await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token))[0].LastError);
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
