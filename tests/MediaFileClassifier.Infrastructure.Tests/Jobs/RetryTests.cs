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
/// Tests for automatic retries with backoff in <see cref="JobScheduler"/> (E05-S04-T02).
/// </summary>
public sealed class RetryTests : IAsyncLifetime
{
    private static readonly RetryPolicy FastPolicy = new(5, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100)) { NextRandom = () => 1 };

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
    public async Task FlakyEndpoint_EventuallySucceeds_WithoutUserAction()
    {
        // A local server that refuses the first three requests, then answers.
        var calls = 0;
        var handler = new RecordingStage("describe", (_, _) => Interlocked.Increment(ref calls) <= 3
            ? throw new AiProviderException(AiFailureKind.Unreachable, "lm-studio", "localhost:1234 isn't answering.")
            : Task.CompletedTask);
        Start(handler, FastPolicy);

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.AiAnalysis, [new WorkItemRequest("describe")]), Token);

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        var item = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token));
        Assert.Equal(WorkItemState.Completed, item.State);
        Assert.Equal(3, item.RetryCount);
        Assert.Equal(4, item.AttemptCount);
        Assert.Null(item.RetryAt);
        Assert.Empty(handler.Failures);
    }

    [Fact]
    public async Task TransientFailure_ThatNeverPasses_FailsAfterTheLastAttempt()
    {
        var handler = new RecordingStage("read", (_, _) => throw new IOException("in use", unchecked((int)0x80070020)));
        Start(handler, FastPolicy);

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.Import, [new WorkItemRequest("read")]), Token);

        await WaitForJobAsync(jobId, ProcessingJobState.Failed);
        var item = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token));
        Assert.Equal(WorkItemState.Failed, item.State);
        Assert.Equal(ErrorCategory.Transient, item.ErrorCategory);
        Assert.Equal(FastPolicy.MaxAttempts, item.AttemptCount);
        Assert.Equal(FastPolicy.MaxAttempts - 1, item.RetryCount);
        var failure = Assert.Single(handler.Failures);
        Assert.Equal(ErrorCategory.Transient, failure.Category);
    }

    [Fact]
    public async Task PermanentFailure_FailsAtOnce_AndTellsTheStage()
    {
        var handler = new RecordingStage("decode", (_, _) => throw new InvalidDataException("damaged"));
        Start(handler, FastPolicy);

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.Import, [new WorkItemRequest("decode")]), Token);

        await WaitForJobAsync(jobId, ProcessingJobState.Failed);
        var item = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token));
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(0, item.RetryCount);
        Assert.Equal(ErrorCategory.Permanent, item.ErrorCategory);
        Assert.Equal("damaged", item.LastError);
        Assert.Equal("damaged", Assert.Single(handler.Failures).Message);
    }

    [Fact]
    public async Task Retry_WaitsForItsBackoff()
    {
        var calls = 0;
        var handler = new RecordingStage("describe", (_, _) => Interlocked.Increment(ref calls) == 1
            ? throw new TimeoutException()
            : Task.CompletedTask);
        Start(handler, new RetryPolicy(5, TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(1)) { NextRandom = () => 1 });
        var stopwatch = Stopwatch.StartNew();

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.AiAnalysis, [new WorkItemRequest("describe")]), Token);

        await WaitForJobAsync(jobId, ProcessingJobState.Completed);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(550), $"The retry ran after {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task RetryAt_KeepsAQueuedRetryOutOfTheQueueUntilItsDue()
    {
        var handler = new RecordingStage("describe", (_, _) => throw new TimeoutException());
        Start(handler, new RetryPolicy(5, TimeSpan.FromHours(1), TimeSpan.FromHours(1)) { NextRandom = () => 1 });

        var jobId = await scheduler!.EnqueueJobAsync(JobSchedulerTests.Job(JobPriority.AiAnalysis, [new WorkItemRequest("describe")]), Token);

        WorkItem item;
        var stopwatch = Stopwatch.StartNew();
        while ((item = Assert.Single(await bed.Library.Jobs.GetWorkItemsAsync(jobId, Token))).RetryAt is null)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), "The work item wasn't scheduled for a retry.");
            await Task.Delay(20, Token);
        }

        await Task.Delay(300, Token);
        Assert.Single(handler.Started);
        Assert.Equal(WorkItemState.Queued, item.State);
        Assert.Equal(1, item.RetryCount);
        Assert.Equal(ErrorCategory.Transient, item.ErrorCategory);
        Assert.Empty(await bed.Library.Jobs.GetQueuedAsync(ResourcePool.Ai, 10, [], null, DateTimeOffset.UtcNow, Token));
        Assert.Equal(item.RetryAt, await bed.Library.Jobs.GetNextRetryAtAsync(DateTimeOffset.UtcNow, Token));
    }

    [Fact]
    public void RetryPolicy_DoublesUpToTheCap_WithFullJitter()
    {
        var policy = RetryPolicy.Default with { NextRandom = () => 1 };

        Assert.Equal([2, 4, 8, 16, 32, 60, 60], Enumerable.Range(0, 7).Select(retry => policy.GetDelay(retry).TotalSeconds));
        Assert.Equal(TimeSpan.Zero, (policy with { NextRandom = () => 0 }).GetDelay(3));
        Assert.True(policy.CanRetry(3));
        Assert.False(policy.CanRetry(4));
    }

    private void Start(IStageHandler handler, RetryPolicy policy)
    {
        var limits = new Dictionary<ResourcePool, int> { [ResourcePool.Io] = 2, [ResourcePool.Cpu] = 2, [ResourcePool.Ai] = 1 };
        var classifier = new ErrorClassifier([new AiExceptionClassifier()]);
        scheduler = new JobScheduler(bed.Library, [handler], limits, new JobProgressTracker(TimeProvider.System), TimeProvider.System, NullLogger.Instance, null, classifier, policy);
        scheduler.Start(startPaused: false);
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

    private sealed class RecordingStage(string stage, Func<WorkItemContext, CancellationToken, Task> work) : IFailureAwareStage
    {
        public string Stage => stage;

        public ResourcePool Pool => ResourcePool.Ai;

        public ConcurrentQueue<WorkItem> Started { get; } = new();

        public ConcurrentQueue<ClassifiedError> Failures { get; } = new();

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            Started.Enqueue(context.WorkItem);
            return work(context, cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, ClassifiedError error, CancellationToken cancellationToken)
        {
            Failures.Enqueue(error);
            return Task.CompletedTask;
        }
    }
}
