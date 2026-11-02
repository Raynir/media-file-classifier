using System.Diagnostics;
using System.Globalization;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for <see cref="JobSystem"/>: restart recovery and the scheduler's lifetime (E05-S01-T05, T06).
/// </summary>
public sealed class JobSystemTests : IAsyncLifetime
{
    private readonly TestSettings settings = new();
    private LibraryTestBed bed = null!;
    private JobSystem? system;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (system is not null)
        {
            await system.StopAsync();
            system.Dispose();
        }

        await bed.DisposeAsync();
    }

    [Theory]
    [InlineData(ResourcePool.Io, "import")]
    [InlineData(ResourcePool.Ai, "analyze")]
    public async Task StartAsync_AfterTheAppWasKilled_ResumesTheInterruptedWork(ResourcePool pool, string stage)
    {
        // As the killed app left it: one work item done, one running, three queued.
        var job = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = stage, Description = stage, Priority = (int)JobPriority.Import, CreatedAt = LibraryTestBed.Start };
        var items = Enumerable.Range(0, 5).Select(i => WorkItem.Create(job.Id, null, stage, pool, job.Priority, $"{i}", LibraryTestBed.Start.AddMilliseconds(i))).ToList();
        await bed.Library.Jobs.AddJobAsync(job, items, Token);
        var done = await bed.Library.Jobs.StartAsync(items[0], LibraryTestBed.Start, Token);
        await bed.Library.Jobs.FinishAsync(done!.ChangeState(WorkItemState.Completed, LibraryTestBed.Start), LibraryTestBed.Start, Token);
        await bed.Library.Jobs.StartAsync(items[1], LibraryTestBed.Start, Token);
        await bed.ReopenAsync();

        var handler = new TestStageHandler(stage, pool);
        system = CreateSystem(handler);
        await system.StartAsync(bed.Library, Token);
        await JobSchedulerTests.WaitUntilAsync(() => bed.Library.Jobs.GetJobAsync(job.Id, Token).Result!.State == ProcessingJobState.Completed);

        Assert.Equal(["1", "2", "3", "4"], handler.Started.Select(item => item.Payload).Order());
        var stored = await bed.Library.Jobs.GetWorkItemsAsync(job.Id, Token);
        Assert.All(stored, item => Assert.Equal(WorkItemState.Completed, item.State));
        Assert.Equal(2, stored.Single(item => item.Payload == "1").AttemptCount);
    }

    [Fact]
    public async Task StartAsync_AskModeWithUnfinishedWork_WaitsForResume()
    {
        settings.Current = new AppSettings { Processing = new ProcessingSettings { ResumeOnOpen = ResumeMode.Ask } };
        var job = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = "t", Description = "t", CreatedAt = LibraryTestBed.Start };
        await bed.Library.Jobs.AddJobAsync(job, [WorkItem.Create(job.Id, null, "stage", ResourcePool.Cpu, 0, null, LibraryTestBed.Start)], Token);
        var handler = new TestStageHandler("stage", ResourcePool.Cpu);
        system = CreateSystem(handler);

        var scheduler = await system.StartAsync(bed.Library, Token);
        await Task.Delay(200, Token);

        Assert.True(scheduler.IsPaused);
        Assert.Empty(handler.Started);
        Assert.Equal(1, await system.CountPendingAsync(Token));

        scheduler.Resume();
        await JobSchedulerTests.WaitUntilAsync(() => handler.Started.Count == 1);
    }

    [Fact]
    public async Task StartAsync_AskModeWithNothingPending_DoesNotPause()
    {
        settings.Current = new AppSettings { Processing = new ProcessingSettings { ResumeOnOpen = ResumeMode.Ask } };
        system = CreateSystem(new TestStageHandler("stage", ResourcePool.Cpu));

        Assert.False((await system.StartAsync(bed.Library, Token)).IsPaused);
    }

    [Fact]
    public async Task StopAsync_ClearsTheCurrentScheduler()
    {
        system = CreateSystem(new TestStageHandler("stage", ResourcePool.Cpu));
        await system.StartAsync(bed.Library, Token);

        await system.StopAsync();

        Assert.Null(system.Current);
        Assert.Equal(0, await system.CountPendingAsync(Token));
    }

    [Fact]
    public async Task StopAsync_DuringHeavyProcessing_StopsWithinSeconds_AndResumesWithoutLosingWork()
    {
        // Every third item is slow, like an AI call; the others are quick. Each commits one write at the end.
        var media = Enumerable.Range(0, 120).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(media, Token);
        async Task Work(WorkItemContext context, CancellationToken token)
        {
            await Task.Delay(int.Parse(context.WorkItem.Payload!, CultureInfo.InvariantCulture) % 3 == 0 ? 60_000 : 20, token);
            var item = (await context.Library.Items.GetAsync(context.WorkItem.ItemId!.Value, token))!;
            item.SetFavorite(true, DateTimeOffset.UtcNow);
            await context.Library.Items.UpdateAsync(item, token);
        }

        system = CreateSystem(new TestStageHandler("work", ResourcePool.Cpu, Work));
        var scheduler = await system.StartAsync(bed.Library, Token);
        var jobId = await scheduler.EnqueueJobAsync(
            JobSchedulerTests.Job(JobPriority.Import, media.Select((item, index) => new WorkItemRequest("work", item.Id, $"{index}"))),
            Token);
        await Task.Delay(300, Token);

        var stopwatch = Stopwatch.StartNew();
        await system.StopAsync();
        stopwatch.Stop();
        await bed.ReopenAsync();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(4));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
        var stored = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        Assert.DoesNotContain(stored, item => item.State == WorkItemState.Running);
        foreach (var workItem in stored)
        {
            // Completed work has its result; work that was stopped left none.
            var item = await bed.Library.Items.GetAsync(workItem.ItemId!.Value, Token);
            Assert.Equal(workItem.State == WorkItemState.Completed, item!.IsFavorite);
        }

        Assert.Contains(stored, item => item.State == WorkItemState.Queued);
    }

    [Fact]
    public void GetPoolLimits_UsesSettingsOrDefaults()
    {
        var hardware = new TestHardware(6);

        var defaults = JobSystem.GetPoolLimits(new ProcessingSettings(), hardware);
        var configured = JobSystem.GetPoolLimits(new ProcessingSettings { IoConcurrency = 2, CpuConcurrency = 3, AiConcurrency = 0 }, hardware);

        Assert.Equal(4, defaults[ResourcePool.Io]);
        Assert.Equal(6, defaults[ResourcePool.Cpu]);
        Assert.Equal(1, defaults[ResourcePool.Ai]);
        Assert.Equal(2, configured[ResourcePool.Io]);
        Assert.Equal(3, configured[ResourcePool.Cpu]);
        Assert.Equal(1, configured[ResourcePool.Ai]);
    }

    private JobSystem CreateSystem(params IStageHandler[] handlers) =>
        new(handlers, settings, new TestHardware(2), TimeProvider.System, NullLogger<JobSystem>.Instance);

    private sealed class TestSettings : ISettingsService
    {
        public AppSettings Current { get; set; } = new();

        public void Save(AppSettings settings) => Current = settings;

        public AppSettings Update(Func<AppSettings, AppSettings> change) => Current = change(Current);
    }

    private sealed class TestHardware(int parallelism) : IHardwareInfo
    {
        public HardwareProfile Profile { get; } = new("X64", parallelism, 8L << 30, []);

        public ExecutionProvider PreferredExecutionProvider => ExecutionProvider.Cpu;

        public int DefaultParallelism => parallelism;
    }
}
