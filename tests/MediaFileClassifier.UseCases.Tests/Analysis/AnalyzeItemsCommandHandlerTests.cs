using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalyzeItemsCommandHandler"/>: the job it queues (E06-S01, E09-S03-T03).
/// </summary>
public sealed class AnalyzeItemsCommandHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, JobPriority.AiAnalysis)]
    [InlineData(JobPriority.UserAction, JobPriority.UserAction)]
    public async Task Handle_QueuesTheJob_WithTheRequestedPriority(JobPriority? requested, JobPriority expected)
    {
        var scheduler = new RecordingScheduler();
        var handler = new AnalyzeItemsCommandHandler(new FakeJobSystem(scheduler), NullLogger<AnalyzeItemsCommandHandler>.Instance);
        var itemId = Id<MediaItem>.New();
        var command = requested is { } priority
            ? new AnalyzeItemsCommand([itemId], Priority: priority)
            : new AnalyzeItemsCommand([itemId]);

        var jobId = await handler.Handle(command, Token);

        Assert.NotNull(jobId);
        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(expected, request.Priority);
        Assert.Equal([itemId], request.ItemIds);
    }

    [Fact]
    public async Task Handle_WithoutBackgroundProcessing_QueuesNothing()
    {
        var handler = new AnalyzeItemsCommandHandler(new FakeJobSystem(null), NullLogger<AnalyzeItemsCommandHandler>.Instance);

        var jobId = await handler.Handle(new AnalyzeItemsCommand([Id<MediaItem>.New()], Priority: JobPriority.UserAction), Token);

        Assert.Null(jobId);
    }

    private sealed class FakeJobSystem(IJobScheduler? scheduler) : IJobSystem
    {
        public IJobScheduler? Current => scheduler;

        public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync() => Task.CompletedTask;

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class RecordingScheduler : IJobScheduler
    {
        public List<JobRequest> Requests { get; } = [];

        public IOpenLibrary Library => throw new NotSupportedException();

        public bool IsPaused => false;

        public Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Id<ProcessingJob>.New());
        }

        public Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds)
        {
        }

        public int GetPoolLimit(ResourcePool pool) => 1;

        public void SetPoolLimit(ResourcePool pool, int limit)
        {
        }

        public void Resume()
        {
        }
    }
}
