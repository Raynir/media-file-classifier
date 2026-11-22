using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Work items counted by <see cref="JobProgressTracker.AddWork"/> while they're being stored. Pass it to
/// <see cref="JobProgressTracker.EndAddWork"/> once the write is over, with the items that weren't stored.
/// </summary>
public sealed class WorkAddition
{
    internal WorkAddition(Id<ProcessingJob> jobId, int count, object counters)
    {
        JobId = jobId;
        Count = count;
        Counters = counters;
    }

    /// <summary>
    /// Gets the job.
    /// </summary>
    public Id<ProcessingJob> JobId { get; }

    /// <summary>
    /// Gets how many work items were counted.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the tracker's counters the items were added to. If the job has other counters by the time the write ends
    /// (the library was reopened, or the job was cancelled or finished and tracked again), they aren't touched.
    /// </summary>
    internal object Counters { get; }

    /// <summary>
    /// Gets or sets whether <see cref="JobProgressTracker.EndAddWork"/> was called already.
    /// </summary>
    internal bool IsEnded { get; set; }
}
