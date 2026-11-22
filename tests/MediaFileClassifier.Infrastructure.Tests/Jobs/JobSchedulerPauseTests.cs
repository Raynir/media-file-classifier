using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static MediaFileClassifier.Infrastructure.Tests.Jobs.JobSchedulerTests;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for pausing and resuming the <see cref="JobScheduler"/> globally, per pool and per job (E05-S05-T01).
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class JobSchedulerPauseTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private JobScheduler? scheduler;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (scheduler is not null)
        {
            await scheduler.DisposeAsync();
        }

        await bed.DisposeAsync();
    }

    [Fact]
    public async Task Pause_LetsRunningWorkFinishWithinTheGrace_AndStartsNothingNew()
    {
        using var release = new SemaphoreSlim(0);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu, (_, token) => release.WaitAsync(token));
        Start([handler], cpu: 1);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 3)), Token);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 1);

        scheduler.Pause();
        release.Release(3);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 0);
        await Task.Delay(200, Token);

        Assert.True(scheduler.IsPaused);
        Assert.Single(handler.Started);
        var states = (await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).Select(item => item.State).ToList();
        Assert.Equal(1, states.Count(state => state == WorkItemState.Completed));
        Assert.Equal(2, states.Count(state => state == WorkItemState.Queued));

        scheduler.Resume();
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Equal(3, handler.Started.Count);
    }

    [Fact]
    public async Task Pause_CancelsWorkStillRunningAfterTheGrace_AndQueuesItAgain_WithoutLosingIt()
    {
        var media = bed.NewItem(0);
        await bed.Library.Items.AddRangeAsync([media], Token);
        var runs = 0;
        var handler = new TestStageHandler("analyze", ResourcePool.Ai, async (context, token) =>
        {
            // The first run is a long analysis; it's interrupted by the pause and redone after Resume.
            if (Interlocked.Increment(ref runs) == 1)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }

            var item = (await context.Library.Items.GetAsync(context.WorkItem.ItemId!.Value, token))!;
            item.SetFavorite(true, DateTimeOffset.UtcNow);
            await context.Library.Items.UpdateAsync(item, token);
        });
        Start([handler]);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.AiAnalysis, [new WorkItemRequest("analyze", media.Id)]), Token);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Ai) == 1);

        var stopwatch = Stopwatch.StartNew();
        scheduler.Pause();
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Ai) == 0, TimeSpan.FromSeconds(30));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= JobScheduler.StopGracePeriod - TimeSpan.FromMilliseconds(100), $"Stopped after {stopwatch.Elapsed}, before the grace ended.");
        var workItem = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token));
        Assert.Equal(WorkItemState.Queued, workItem.State);
        Assert.False(ProcessingJobStateTransitions.IsFinished((await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State));
        Assert.False((await bed.Library.Items.GetAsync(media.Id, Token))!.IsFavorite);

        scheduler.Resume();
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Equal(2, runs);
        Assert.True((await bed.Library.Items.GetAsync(media.Id, Token))!.IsFavorite);
    }

    [Fact]
    public async Task ResumeWithinTheGrace_LetsRunningWorkRunOn()
    {
        using var release = new SemaphoreSlim(0);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu, (_, token) => release.WaitAsync(token));
        Start([handler], cpu: 1);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 1);

        scheduler.Pause();
        scheduler.Resume();
        await Task.Delay(JobScheduler.StopGracePeriod + TimeSpan.FromMilliseconds(500), Token);

        Assert.Equal(1, scheduler.GetRunningCount(ResourcePool.Cpu));
        release.Release();
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Single(handler.Started);
    }

    [Fact]
    public async Task PausePool_HoldsOnlyThatPool_UntilItsResumed()
    {
        var ai = new TestStageHandler("ai", ResourcePool.Ai);
        var cpu = new TestStageHandler("cpu", ResourcePool.Cpu);
        Start([ai, cpu]);

        scheduler!.PausePool(ResourcePool.Ai);
        var jobId = await scheduler.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("ai"), new WorkItemRequest("cpu"), new WorkItemRequest("cpu")]), Token);
        await WaitUntilAsync(() => cpu.Started.Count == 2);
        await Task.Delay(200, Token);

        Assert.True(scheduler.IsPoolPaused(ResourcePool.Ai));
        Assert.False(scheduler.IsPoolPaused(ResourcePool.Cpu));
        Assert.False(scheduler.IsPaused);
        Assert.Empty(ai.Started);

        scheduler.ResumePool(ResourcePool.Ai);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.Single(ai.Started);
    }

    [Fact]
    public async Task PauseJobAsync_HoldsOnlyThatJob_IncludingWorkAddedLater_AndStoresTheState()
    {
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        Start([handler], startPaused: true);
        var held = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 2)), Token);
        var other = await scheduler.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 2)), Token);

        Assert.True(await scheduler.PauseJobAsync(held, Token));
        Assert.True(await scheduler.EnqueueWorkAsync(held, [new WorkItemRequest("stage")], Token));
        scheduler.Resume();
        await WaitForJobAsync(other, ProcessingJobState.Completed);
        await Task.Delay(200, Token);

        Assert.Equal(ProcessingJobState.Paused, (await bed.Library.Jobs.GetJobAsync(held, Token))!.State);
        Assert.DoesNotContain(handler.Started, item => item.JobId == held);
        Assert.Equal(0, await bed.Library.Jobs.CountPendingAsync(Token));

        Assert.True(await scheduler.ResumeJobAsync(held, Token));
        await WaitForJobAsync(held, ProcessingJobState.Completed);
        Assert.Equal(3, handler.Started.Count(item => item.JobId == held));
    }

    [Fact]
    public async Task PauseJobAsync_WhileItsLastWorkRuns_FinishesTheJobWhenItCompletes()
    {
        using var release = new SemaphoreSlim(0);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu, (_, token) => release.WaitAsync(token));
        Start([handler]);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 1);

        Assert.True(await scheduler.PauseJobAsync(jobId, Token));
        release.Release();

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
    }

    [Fact]
    public async Task PauseJobAsync_AndResumeJobAsync_RefuseJobsInTheWrongState()
    {
        Start([new TestStageHandler("stage", ResourcePool.Cpu)]);
        var finished = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        await WaitForJobAsync(finished, ProcessingJobState.Completed);

        Assert.False(await scheduler.PauseJobAsync(finished, Token));
        Assert.False(await scheduler.PauseJobAsync(Id<ProcessingJob>.New(), Token));
        Assert.False(await scheduler.ResumeJobAsync(finished, Token));
        Assert.False(await scheduler.ResumeJobAsync(Id<ProcessingJob>.New(), Token));
    }

    [Fact]
    public async Task CancelJobAsync_OfAPausedJob_CancelsItsQueuedWork()
    {
        Start([new TestStageHandler("stage", ResourcePool.Cpu)], startPaused: true);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 2)), Token);
        Assert.True(await scheduler.PauseJobAsync(jobId, Token));

        Assert.True(await scheduler.CancelJobAsync(jobId, Token));

        Assert.Equal(ProcessingJobState.Cancelled, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
        Assert.All(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token), item => Assert.Equal(WorkItemState.Cancelled, item.State));
    }

    [Fact]
    public async Task PauseChanged_IsRaisedOncePerChange_AtEveryLevel()
    {
        Start([new TestStageHandler("stage", ResourcePool.Cpu)], startPaused: true);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        var raised = 0;
        scheduler.PauseChanged += (_, _) => Interlocked.Increment(ref raised);

        scheduler.Resume();
        scheduler.Resume();
        scheduler.Pause();
        scheduler.Pause();
        scheduler.PausePool(ResourcePool.Io);
        scheduler.PausePool(ResourcePool.Io);
        scheduler.ResumePool(ResourcePool.Io);
        scheduler.ResumePool(ResourcePool.Io);
        await scheduler.PauseJobAsync(jobId, Token);
        await scheduler.PauseJobAsync(jobId, Token);
        await scheduler.ResumeJobAsync(jobId, Token);
        await scheduler.ResumeJobAsync(jobId, Token);

        Assert.Equal(6, raised);
    }

    private void Start(IEnumerable<IStageHandler> handlers, int io = 4, int cpu = 2, int ai = 1, bool startPaused = false)
    {
        var limits = new Dictionary<ResourcePool, int> { [ResourcePool.Io] = io, [ResourcePool.Cpu] = cpu, [ResourcePool.Ai] = ai };
        scheduler = new JobScheduler(bed.Library, handlers, limits, new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance);
        scheduler.Start(startPaused);
    }

    private async Task WaitForJobAsync(Id<ProcessingJob> jobId, ProcessingJobState state)
    {
        var stopwatch = Stopwatch.StartNew();
        while ((await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State != state)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Timed out waiting for the job to be {state}.");
            await Task.Delay(20, Token);
        }
    }
}
