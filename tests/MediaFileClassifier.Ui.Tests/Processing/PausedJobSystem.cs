using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// A job system whose scheduler waits for <see cref="IJobScheduler.Resume"/>, as after opening a library with unfinished
/// work when the settings say to ask first.
/// </summary>
internal sealed class PausedJobSystem : IJobSystem, IJobScheduler
{
    public IJobScheduler? Current => this;

    public IOpenLibrary Library => throw new NotSupportedException();

    public bool IsPaused { get; private set; } = true;

    public int ResumeCount { get; private set; }

    public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task StopAsync() => Task.CompletedTask;

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public void Resume()
    {
        IsPaused = false;
        ResumeCount++;
    }

    public Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds)
    {
    }

    public int GetPoolLimit(ResourcePool pool) => 1;

    public void SetPoolLimit(ResourcePool pool, int limit)
    {
    }

    public void ReleaseParked()
    {
    }
}
