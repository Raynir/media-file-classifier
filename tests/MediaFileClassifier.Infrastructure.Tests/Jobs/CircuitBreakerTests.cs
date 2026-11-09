using System.Collections.Concurrent;
using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Jobs.Errors;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// Tests for the provider circuit breaker and the parking of work in <see cref="JobScheduler"/> (E05-S04-T03).
/// </summary>
public sealed class CircuitBreakerTests : IAsyncLifetime
{
    private const string LmStudio = "lm-studio";

    private static readonly RetryPolicy FastPolicy = new(5, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100)) { NextRandom = () => 1 };
    private static readonly TimeSpan FastProbe = TimeSpan.FromMilliseconds(100);

    private readonly SwitchableProvider provider = new(LmStudio);
    private readonly AiProviderRegistry registry;
    private readonly ProviderCircuitBreaker breaker;
    private LibraryTestBed bed = null!;
    private JobScheduler? scheduler;

    public CircuitBreakerTests()
    {
        registry = new AiProviderRegistry([provider], TimeProvider.System);
        breaker = new ProviderCircuitBreaker(TimeProvider.System, null, registry, FastProbe);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (scheduler is not null)
        {
            await scheduler.DisposeAsync();
        }

        breaker.Dispose();
        await bed.DisposeAsync();
    }

    [Fact]
    public void Breaker_OpensAfterThreeConsecutiveProviderFailures()
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));

        Assert.False(slow.RecordFailure(Unreachable()));
        Assert.False(slow.RecordFailure(Unreachable()));
        Assert.False(slow.IsOpen(LmStudio));
        Assert.True(slow.RecordFailure(Unreachable()));

        Assert.True(slow.IsOpen(LmStudio));
        var status = Assert.Single(slow.OpenCircuits);
        Assert.Equal("Switchable lm-studio", status.DisplayName);
        Assert.Equal("localhost:1234 isn't answering.", status.Message);
        Assert.False(status.IsConfiguration);
    }

    [Fact]
    public void Breaker_SuccessResetsTheCount()
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));

        slow.RecordFailure(Unreachable());
        slow.RecordFailure(Unreachable());
        slow.RecordSuccess(LmStudio);
        slow.RecordFailure(Unreachable());
        slow.RecordFailure(Unreachable());

        Assert.False(slow.IsOpen(LmStudio));
    }

    [Fact]
    public void Breaker_ConfigurationProblem_OpensAtOnce()
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        var error = new ClassifiedError(ErrorCategory.Configuration, "Confirm the host.", new InvalidOperationException()) { ProviderId = LmStudio, IsProviderFailure = true };

        Assert.True(slow.RecordFailure(error));
        Assert.True(Assert.Single(slow.OpenCircuits).IsConfiguration);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Breaker_IgnoresItemFailuresAndMissingModels(bool isProviderFailure, bool isModelMissing)
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        var error = new ClassifiedError(ErrorCategory.Transient, "x", new InvalidOperationException())
        {
            ProviderId = LmStudio,
            IsProviderFailure = isProviderFailure,
            IsModelMissing = isModelMissing,
        };

        for (var i = 0; i < 5; i++)
        {
            Assert.False(slow.RecordFailure(error));
        }

        Assert.Empty(slow.OpenCircuits);
    }

    [Fact]
    public async Task Breaker_ProbeKeepsItOpenWhileDown_AndClosesWhenHealthy()
    {
        var changes = 0;
        breaker.Changed += (_, _) => Interlocked.Increment(ref changes);
        provider.IsUp = false;
        Trip(breaker);
        Assert.Equal(1, changes);

        await Task.Delay(FastProbe * 4, Token);
        Assert.True(breaker.IsOpen(LmStudio));
        Assert.Equal("Switchable lm-studio is down.", Assert.Single(breaker.OpenCircuits).Message);

        provider.IsUp = true;
        await WaitUntilAsync(() => !breaker.IsOpen(LmStudio), "The circuit didn't close.");
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task Breaker_CheckNow_ClosesAtOnce()
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        provider.IsUp = true;
        Trip(slow);

        await slow.CheckNowAsync(Token);

        Assert.False(slow.IsOpen(LmStudio));
    }

    [Fact]
    public async Task Breaker_RemovedProvider_Closes()
    {
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        provider.IsUp = false;
        Trip(slow);
        registry.Unregister(LmStudio);

        await slow.CheckNowAsync(Token);

        Assert.False(slow.IsOpen(LmStudio));
    }

    [Fact]
    public void Breaker_AfterDispose_RecordsNothing()
    {
        var disposed = new ProviderCircuitBreaker(TimeProvider.System, null, registry, FastProbe);
        disposed.Dispose();

        Trip(disposed);

        Assert.Empty(disposed.OpenCircuits);
    }

    [Fact]
    public async Task StoppingTheProvider_PausesCleanly_AndStartingItResumesWithNoFailedItems()
    {
        // The Done-when of E05-S04-T03: LM Studio stops mid-analysis, then starts again.
        var providerCalls = 0;
        var stage = new ProviderStage("describe", (_, _) =>
        {
            if (breaker.IsOpen(LmStudio))
            {
                // What a stage does when the breaker says the provider is unavailable: skip the call, and say so.
                throw new AiProviderException(AiFailureKind.Unreachable, LmStudio, "LM Studio is unavailable.");
            }

            Interlocked.Increment(ref providerCalls);
            if (!provider.IsUp)
            {
                throw new AiProviderException(AiFailureKind.Unreachable, LmStudio, "localhost:1234 isn't answering.");
            }

            return Task.CompletedTask;
        });
        var opened = new TaskCompletionSource();
        breaker.Changed += (_, _) =>
        {
            if (breaker.IsOpen(LmStudio))
            {
                opened.TrySetResult();
            }
        };
        provider.IsUp = false;
        Start(stage);

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.AiAnalysis, [.. Enumerable.Range(0, 10).Select(_ => new WorkItemRequest("describe"))]), Token);
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        await WaitUntilAsync(async () => (await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).All(item => item.IsParked), "Not every work item was parked.");

        await Task.Delay(FastProbe * 3, Token);
        var parked = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        Assert.All(parked, item => Assert.True(item.IsParked));
        Assert.Equal(ProcessingJobState.Running, (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State);
        Assert.Empty(stage.Failures);

        var callsWhileDown = Volatile.Read(ref providerCalls);
        Assert.Equal(ProviderCircuitBreaker.FailureThreshold, callsWhileDown);

        provider.IsUp = true;
        await WaitForJobAsync(jobId, ProcessingJobState.Completed);

        var items = await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token);
        Assert.All(items, item => Assert.Equal(WorkItemState.Completed, item.State));
        Assert.All(items, item => Assert.True(item.RetryCount < FastPolicy.MaxAttempts - 1));
        Assert.Empty(stage.Failures);
    }

    [Fact]
    public async Task ParkedWork_IsReleasedWhenTheLibraryOpensAgain()
    {
        // Circuits live in memory, so work parked before a restart runs again after it.
        var calls = 0;
        var stage = new ProviderStage("describe", (_, _) => Interlocked.Increment(ref calls) == 1
            ? throw new AiProviderException(AiFailureKind.RemoteHostNotConfirmed, LmStudio, "Confirm the host.")
            : Task.CompletedTask);
        using var slow = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        Start(stage, slow);
        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.AiAnalysis, [new WorkItemRequest("describe")]), Token);
        await WaitUntilAsync(async () => Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).IsParked, "The work item wasn't parked.");
        await scheduler.DisposeAsync();

        using var fresh = new ProviderCircuitBreaker(TimeProvider.System, null, registry, TimeSpan.FromHours(1));
        Start(stage, fresh);

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
    }

    [Fact]
    public async Task ParkedWork_IsNoRetryDue_AndIsReleasedOnRequest()
    {
        var jobId = Id<ProcessingJob>.New();
        var now = DateTimeOffset.UtcNow;
        var parked = WorkItem.Create(jobId, null, "describe", ResourcePool.Ai, 0, null, now)
            .ChangeState(WorkItemState.Running, now)
            .Park(ErrorCategory.Transient, "down", now);
        await bed.Library.Jobs.AddJobAsync(new ProcessingJob { Id = jobId, Type = "test", Description = "test", CreatedAt = now }, [parked], Token);

        Assert.True(Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token)).IsParked);
        Assert.Null(await bed.Library.Jobs.GetNextRetryAtAsync(now, Token));
        Assert.Empty(await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Ai, 10, [], null, now, Token));

        Assert.Equal(1, await bed.Library.Jobs.ReleaseParkedAsync(now, Token));

        Assert.Single(await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Ai, 10, [], null, now.AddSeconds(1), Token));
        Assert.Equal(0, await bed.Library.Jobs.ReleaseParkedAsync(now, Token));
    }

    private static ClassifiedError Unreachable() =>
        new(ErrorCategory.Transient, "localhost:1234 isn't answering.", new AiProviderException()) { ProviderId = LmStudio, IsProviderFailure = true };

    private static void Trip(ProviderCircuitBreaker target)
    {
        for (var i = 0; i < ProviderCircuitBreaker.FailureThreshold; i++)
        {
            target.RecordFailure(Unreachable());
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string message)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), message);
            await Task.Delay(20, Token);
        }
    }

    private static Task WaitUntilAsync(Func<bool> condition, string message) => WaitUntilAsync(() => Task.FromResult(condition()), message);

    private void Start(IStageHandler stage, IProviderCircuitBreaker? withBreaker = null)
    {
        var limits = new Dictionary<ResourcePool, int> { [ResourcePool.Io] = 2, [ResourcePool.Cpu] = 2, [ResourcePool.Ai] = 1 };
        var classifier = new ErrorClassifier([new AiExceptionClassifier()]);
        scheduler = new JobScheduler(bed.Library, [stage], limits, new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance, null, classifier, FastPolicy, withBreaker ?? breaker);
        scheduler.Start(startPaused: false);
    }

    private async Task WaitForJobAsync(Id<ProcessingJob> jobId, ProcessingJobState state) =>
        await WaitUntilAsync(async () => (await bed.Library.Jobs.GetJobAsync(jobId, Token))!.State == state, $"Timed out waiting for the job to be {state}.");

    /// <summary>
    /// A provider that's up or down, like LM Studio being started and stopped.
    /// </summary>
    private sealed class SwitchableProvider(string id) : IAiProvider
    {
        private volatile bool isUp = true;

        public bool IsUp
        {
            get => isUp;
            set => isUp = value;
        }

        public string Id => id;

        public string DisplayName => $"Switchable {id}";

        public AiProviderKind Kind => AiProviderKind.Fake;

        public ModelIdentity Model { get; } = new(ProviderIds.Fake, "switchable");

        public bool IsSupportedOnCurrentPlatform => true;

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(IsUp ? ProviderHealth.Healthy : new ProviderHealth(ProviderHealthStatus.Unreachable, $"{DisplayName} is down."));
    }

    private sealed class ProviderStage(string stage, Func<WorkItemContext, CancellationToken, Task> work) : IFailureAwareStage
    {
        public string Stage => stage;

        public ResourcePool Pool => ResourcePool.Ai;

        public ConcurrentQueue<ClassifiedError> Failures { get; } = new();

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken) => work(context, cancellationToken);

        public Task OnFailedAsync(WorkItemContext context, ClassifiedError error, CancellationToken cancellationToken)
        {
            Failures.Enqueue(error);
            return Task.CompletedTask;
        }
    }
}
