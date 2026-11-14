using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for how long finished jobs are kept by <see cref="JobScheduler"/> (E05-S03-T04).
/// </summary>
public sealed class JobRetentionTests : IAsyncLifetime
{
    private static readonly JobRetention KeepOne = new(1, TimeSpan.FromDays(30));

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
    public async Task AJobThatFinishes_PrunesOldFinishedJobs_ButKeepsItself()
    {
        var old = await AddFinishedJobAsync(LibraryTestBed.Start);
        var older = await AddFinishedJobAsync(LibraryTestBed.Start.AddMinutes(-5));
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        scheduler = new JobScheduler(bed.Library, [handler], new Dictionary<ResourcePool, int> { [ResourcePool.Cpu] = 1 }, new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance, retention: KeepOne);
        scheduler.Start(startPaused: false);

        var jobId = await scheduler.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.Import, [new WorkItemRequest("stage")]), Token);

        await JobSchedulerTests.WaitUntilAsync(() => bed.Library.Jobs.GetJobAsync(old, Token).Result is null && bed.Library.Jobs.GetJobAsync(older, Token).Result is null);
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
    }

    [Fact]
    public async Task OpeningALibrary_PrunesOldFinishedJobs_AndLeavesUnfinishedOnes()
    {
        var old = await AddFinishedJobAsync(LibraryTestBed.Start);
        var newest = await AddFinishedJobAsync(LibraryTestBed.Start.AddMinutes(5));
        var waiting = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = "t", Description = "t", CreatedAt = LibraryTestBed.Start };
        await bed.Library.Jobs.AddJobAsync(waiting, [WorkItem.Create(waiting.Id, null, "other", ResourcePool.Cpu, 0, null, LibraryTestBed.Start)], Token);
        using var system = new JobSystem(
            [new TestStageHandler("stage", ResourcePool.Cpu)],
            new FixedSettings(),
            new FakeHardwareInfo(defaultParallelism: 1, cpuCores: 1, memoryBytes: 8L << 30),
            new JobProgressTracker(TimeProvider.System),
            TimeProvider.System,
            NullLogger<JobSystem>.Instance,
            retention: KeepOne);

        await system.StartAsync(bed.Library, Token);
        await system.StopAsync();

        Assert.Null(await bed.Library.Jobs.GetJobAsync(old, Token));
        Assert.NotNull(await bed.Library.Jobs.GetJobAsync(newest, Token));
        Assert.NotNull(await bed.Library.Jobs.GetJobAsync(waiting.Id, Token));
    }

    private async Task<Id<ProcessingJob>> AddFinishedJobAsync(DateTimeOffset finishedAt)
    {
        var job = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = "t", Description = "t", CreatedAt = finishedAt };
        var item = WorkItem.Create(job.Id, null, "stage", ResourcePool.Cpu, 0, null, finishedAt);
        await bed.Library.Jobs.AddJobAsync(job, [item], Token);
        var running = await bed.Library.Jobs.StartAsync(item, finishedAt, Token);
        await bed.Library.Jobs.FinishAsync(running!.ChangeState(WorkItemState.Completed, finishedAt), finishedAt, Token);
        return job.Id;
    }

    private sealed class FixedSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public void Save(AppSettings settings) => Current = settings;

        public AppSettings Update(Func<AppSettings, AppSettings> change) => Current = change(Current);
    }
}
