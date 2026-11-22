using MediaFileClassifier.Testing.Timing;
using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for <see cref="JobScheduler"/> against a real library database (E05-S01-T02).
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class JobSchedulerTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private JobScheduler? scheduler;
    private JobProgressTracker progress = null!;

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
    public async Task EnqueueJobAsync_RunsEveryWorkItem_AndCompletesTheJob()
    {
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        Start([handler]);

        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 20)), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);

        Assert.Equal(20, handler.Started.Count);
        Assert.True(handler.AlwaysOnThreadPool);
        Assert.All(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token), item => Assert.Equal(WorkItemState.Completed, item.State));
    }

    [Fact]
    public async Task MixedLoad_KeepsEveryPoolWithinItsLimit()
    {
        async Task Work(WorkItemContext context, CancellationToken token) => await Task.Delay(5, token);
        var io = new TestStageHandler("io", ResourcePool.Io, Work);
        var cpu = new TestStageHandler("cpu", ResourcePool.Cpu, Work);
        var ai = new TestStageHandler("ai", ResourcePool.Ai, Work);
        Start([io, cpu, ai], io: 3, cpu: 2, ai: 1);

        var requests = Enumerable.Range(0, 60).SelectMany(_ => new[] { new WorkItemRequest("io"), new WorkItemRequest("cpu"), new WorkItemRequest("ai") });
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, requests), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed, TimeSpan.FromSeconds(60));

        Assert.Equal(3, io.MaxConcurrent);
        Assert.Equal(2, cpu.MaxConcurrent);
        Assert.Equal(1, ai.MaxConcurrent);
        Assert.Equal(180, io.Started.Count + cpu.Started.Count + ai.Started.Count);
    }

    [Fact]
    public async Task HigherPriorityWork_RunsFirst()
    {
        // One at a time, so the start order is the run order.
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        Start([handler], cpu: 1, startPaused: true);

        var maintenance = await scheduler!.EnqueueJobAsync(Job(JobPriority.Maintenance, [new WorkItemRequest("stage")]), Token);
        var hashing = await scheduler.EnqueueJobAsync(Job(JobPriority.FullHashing, [new WorkItemRequest("stage")]), Token);
        var user = await scheduler.EnqueueJobAsync(Job(JobPriority.UserAction, [new WorkItemRequest("stage")]), Token);
        var import = await scheduler.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        var analysis = await scheduler.EnqueueJobAsync(Job(JobPriority.AiAnalysis, [new WorkItemRequest("stage")]), Token);
        scheduler.Resume();
        await WaitForJobAsync(maintenance, ProcessingJobState.Completed);

        Assert.Equal([user, import, analysis, hashing, maintenance], handler.Started.Select(item => item.JobId));
    }

    [Fact]
    public async Task FailingHandler_FailsTheWorkItemWithItsMessage_AndTheJobStillCompletes()
    {
        var handler = new TestStageHandler("stage", ResourcePool.Io, (context, _) =>
            context.WorkItem.Payload == "bad" ? throw new InvalidDataException("corrupt file") : Task.CompletedTask);
        Start([handler]);

        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage", Payload: "bad"), new WorkItemRequest("stage")]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);

        var items = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        var bad = items.Single(item => item.Payload == "bad");
        Assert.Equal(WorkItemState.Failed, bad.State);
        Assert.Equal("corrupt file", bad.LastError);
        Assert.Equal(WorkItemState.Completed, items.Single(item => item.Payload is null).State);
    }

    [Fact]
    public async Task EnqueueJobAsync_UnknownStage_Throws()
    {
        Start([new TestStageHandler("stage", ResourcePool.Io)]);

        await Assert.ThrowsAsync<ArgumentException>(() => scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("missing")]), Token));
    }

    [Fact]
    public async Task Handler_CanEnqueueTheNextStage()
    {
        var second = new TestStageHandler("second", ResourcePool.Cpu);
        var first = new TestStageHandler("first", ResourcePool.Io, async (context, token) =>
            await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, [new WorkItemRequest("second", Payload: "next")], token));
        Start([first, second]);

        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("first")]), Token);
        await WaitUntilAsync(() => !second.Started.IsEmpty);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);

        Assert.Equal("next", Assert.Single(second.Started).Payload);
        Assert.Equal((int)JobPriority.Import, second.Started.Single().Priority);
    }

    [Fact]
    public async Task SetPoolLimit_ChangesConcurrencyAtRuntime()
    {
        using var release = new SemaphoreSlim(0);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu, (_, token) => release.WaitAsync(token));
        Start([handler], cpu: 1);
        await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, Enumerable.Repeat(new WorkItemRequest("stage"), 10)), Token);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 1);

        scheduler.SetPoolLimit(ResourcePool.Cpu, 4);
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Cpu) == 4);

        Assert.Equal(4, scheduler.GetPoolLimit(ResourcePool.Cpu));
        release.Release(10);
    }

    [Fact]
    public async Task Boost_RunsWorkForOnScreenItemsFirst_AsTheViewScrolls()
    {
        // A large unprocessed library: thumbnails for 300 items, one at a time.
        using var gate = new SemaphoreSlim(0);
        var handler = new TestStageHandler("thumbnail", ResourcePool.Cpu, (_, token) => gate.WaitAsync(token));
        Start([handler], cpu: 1, startPaused: true);
        var media = Enumerable.Range(0, 300).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(media, Token);
        var user = await scheduler!.EnqueueJobAsync(Job(JobPriority.UserAction, [new WorkItemRequest("thumbnail", media[0].Id)]), Token);
        await scheduler.EnqueueJobAsync(Job(JobPriority.Import, media.Select(item => new WorkItemRequest("thumbnail", item.Id))), Token);

        try
        {
            var firstScreen = media.Skip(250).Take(10).Select(item => item.Id).ToList();
            scheduler.Boost(firstScreen);
            scheduler.Resume();
            gate.Release(11);
            await WaitUntilAsync(() => handler.Started.Count == 12);

            var started = handler.Started.ToList();
            Assert.Equal(user, started[0].JobId);
            Assert.Equivalent(firstScreen, started.Skip(1).Take(10).Select(item => item.ItemId!.Value));

            // Scrolling: the next screen replaces the boost, and runs as soon as the running work item finishes.
            var secondScreen = media.Skip(120).Take(10).Select(item => item.Id).ToList();
            scheduler.Boost(secondScreen);
            gate.Release(10);
            await WaitUntilAsync(() => handler.Started.Count == 22);

            Assert.Equivalent(secondScreen, handler.Started.Skip(12).Take(10).Select(item => item.ItemId!.Value));
        }
        finally
        {
            gate.Release(1000);
        }
    }

    [Fact]
    public async Task CancelJobAsync_DuringAnalysis_StopsWithinSeconds_AndKeepsFinishedResults()
    {
        // An AI stage that commits one write at the end, like a real analysis stage.
        var media = Enumerable.Range(0, 4).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(media, Token);
        var handler = new TestStageHandler("analyze", ResourcePool.Ai, async (context, token) =>
        {
            if (context.WorkItem.Payload != "fast")
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }

            var item = (await context.Library.Items.GetAsync(context.WorkItem.ItemId!.Value, token))!;
            item.SetFavorite(true, DateTimeOffset.UtcNow);
            await context.Library.Items.UpdateAsync(item, token);
        });
        // All four run at once: the fast one finishes, the other three are mid-analysis when the job is cancelled.
        Start([handler], ai: 4);
        var jobId = await scheduler!.EnqueueJobAsync(
            Job(JobPriority.AiAnalysis, media.Select((item, index) => new WorkItemRequest("analyze", item.Id, index == 0 ? "fast" : null))),
            Token);
        await WaitUntilAsync(() => handler.Started.Count == 4 && scheduler.GetRunningCount(ResourcePool.Ai) == 3);

        var stopwatch = Stopwatch.StartNew();
        Assert.True(await scheduler.CancelJobAsync(jobId, Token));
        await WaitUntilAsync(() => scheduler.GetRunningCount(ResourcePool.Ai) == 0);
        stopwatch.Stop();

        // The analysis takes a minute, so cancelling well under that shows it was stopped, not awaited; under 3 s on an
        // idle machine (MFC_PERF_TESTS=1, TestPerformance).
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Cancelling took {stopwatch.Elapsed}.");
        if (TestPerformance.Enabled)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Cancelling took {stopwatch.Elapsed}.");
        }

        Assert.Equal(ProcessingJobState.Cancelled, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
        var states = (await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).ToDictionary(item => item.ItemId!.Value, item => item.State);
        Assert.Equal(WorkItemState.Completed, states[media[0].Id]);
        Assert.All(media.Skip(1), item => Assert.Equal(WorkItemState.Cancelled, states[item.Id]));
        Assert.True((await bed.Library.Items.GetAsync(media[0].Id, Token))!.IsFavorite);
        Assert.All(media.Skip(1), item => Assert.False(bed.Library.Items.GetAsync(item.Id, Token).Result!.IsFavorite));
    }

    [Fact]
    public async Task CancelJobAsync_UnknownOrFinishedJob_ReturnsFalse()
    {
        Start([new TestStageHandler("stage", ResourcePool.Cpu)]);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);

        Assert.False(await scheduler.CancelJobAsync(jobId, Token));
        Assert.False(await scheduler.CancelJobAsync(Id<ProcessingJob>.New(), Token));
    }

    [Fact]
    public async Task Progress_FollowsTheWorkItems_AndForgetsTheFinishedJob()
    {
        using var gate = new SemaphoreSlim(0);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu, async (context, token) =>
        {
            if (context.WorkItem.Payload == "bad")
            {
                throw new InvalidDataException("corrupt file");
            }

            await gate.WaitAsync(token);
        });
        Start([handler], cpu: 3, startPaused: true);

        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage", Payload: "bad"), new WorkItemRequest("stage"), new WorkItemRequest("stage")]), Token);
        Assert.Equal(new StageProgress("stage", 3, 0, 0, true), Assert.Single(progress.Current.Jobs).Stages[0]);

        scheduler.Resume();
        await WaitUntilAsync(() => progress.Current.Jobs is [{ Failed: 1 }]);
        Assert.Equal(2, progress.Current.PendingItems);

        gate.Release(2);
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        await WaitUntilAsync(() => progress.Current.ActiveJobCount == 0);
    }

    [Fact]
    public async Task Progress_ForgetsACancelledJob()
    {
        Start([new TestStageHandler("stage", ResourcePool.Cpu)], startPaused: true);
        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);

        await scheduler.CancelJobAsync(jobId, Token);

        Assert.Same(ProgressSummary.Empty, progress.Current);
    }

    internal static JobRequest Job(JobPriority priority, IEnumerable<WorkItemRequest> workItems) =>
        new("test", "Test job", priority, [.. workItems]);

    internal static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < (timeout ?? TimeSpan.FromSeconds(15)), "Timed out waiting for the condition.");
            await Task.Delay(10, Token);
        }
    }

    private void Start(IEnumerable<IStageHandler> handlers, int io = 4, int cpu = 2, int ai = 1, bool startPaused = false)
    {
        var limits = new Dictionary<ResourcePool, int> { [ResourcePool.Io] = io, [ResourcePool.Cpu] = cpu, [ResourcePool.Ai] = ai };
        scheduler = new JobScheduler(bed.Library, handlers, limits, progress = new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance);
        scheduler.Start(startPaused);
    }

    private async Task WaitForJobAsync(Id<ProcessingJob> jobId, ProcessingJobState state, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while ((await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State != state)
        {
            Assert.True(stopwatch.Elapsed < (timeout ?? TimeSpan.FromSeconds(15)), $"Timed out waiting for the job to be {state}.");
            await Task.Delay(20, Token);
        }
    }
}
