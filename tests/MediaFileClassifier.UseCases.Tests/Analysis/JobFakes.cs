using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Tests.Analysis;

/// <summary>
/// Background processing that runs <see cref="Current"/>, or isn't running when it's <see langword="null"/>.
/// </summary>
internal sealed class FakeJobSystem(IJobScheduler? scheduler) : IJobSystem
{
    public IJobScheduler? Current => scheduler;

    public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task StopAsync() => Task.CompletedTask;

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

/// <summary>
/// A scheduler that records the jobs it's asked to queue.
/// </summary>
internal sealed class RecordingScheduler : IJobScheduler
{
    public List<JobRequest> Requests { get; } = [];

    public IOpenLibrary Library => throw new NotSupportedException();

    public bool IsPaused { get; set; }

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

    /// <summary>
    /// Gets how often the parked work was released.
    /// </summary>
    public int ReleaseCount { get; private set; }

    public void ReleaseParked() => ReleaseCount++;
}
