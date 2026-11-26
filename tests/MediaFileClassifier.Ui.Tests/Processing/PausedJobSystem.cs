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

    public event EventHandler? PauseChanged;

    public void Resume()
    {
        IsPaused = false;
        ResumeCount++;
        PauseChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        IsPaused = true;
        PauseChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsPoolPaused(ResourcePool pool) => PausedPools.Contains(pool);

    public void PausePool(ResourcePool pool)
    {
        PausedPools.Add(pool);
        PauseChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResumePool(ResourcePool pool)
    {
        PausedPools.Remove(pool);
        PauseChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default)
    {
        PausedJobs.Add(jobId);
        PauseChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    public Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default)
    {
        var resumed = PausedJobs.Remove(jobId);
        PauseChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(resumed);
    }

    /// <summary>
    /// Gets the pools paused on their own.
    /// </summary>
    public HashSet<ResourcePool> PausedPools { get; } = [];

    /// <summary>
    /// Gets the jobs paused through <see cref="PauseJobAsync"/>.
    /// </summary>
    public HashSet<Id<ProcessingJob>> PausedJobs { get; } = [];

    public Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> AppendWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
