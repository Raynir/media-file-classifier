using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for <see cref="JobScheduler"/> against a real library database (E05-S01-T02).
/// </summary>
public sealed class JobSchedulerTests : IAsyncLifetime
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
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        Start([handler], startPaused: true);

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
    public async Task FailingHandler_FailsTheWorkItemWithItsMessage_AndTheJob()
    {
        var handler = new TestStageHandler("stage", ResourcePool.Io, (context, _) =>
            context.WorkItem.Payload == "bad" ? throw new InvalidDataException("corrupt file") : Task.CompletedTask);
        Start([handler]);

        var jobId = await scheduler!.EnqueueJobAsync(Job(JobPriority.Import, [new WorkItemRequest("stage", Payload: "bad"), new WorkItemRequest("stage")]), Token);
        await WaitForJobAsync(jobId, ProcessingJobState.Failed);

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
        scheduler = new JobScheduler(bed.Library, handlers, limits, TimeProvider.System, NullLogger.Instance);
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
