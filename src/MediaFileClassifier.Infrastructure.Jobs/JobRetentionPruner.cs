using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Applies <see cref="JobRetention"/> to the jobs of a library (E05-S03-T04).
/// </summary>
internal static class JobRetentionPruner
{
    /// <summary>
    /// Deletes the finished jobs that are over the limits.
    /// </summary>
    /// <param name="jobs">The library's jobs.</param>
    /// <param name="retention">How long finished jobs are kept.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of jobs deleted.</returns>
    public static Task<int> PruneAsync(IJobRepository jobs, JobRetention retention, DateTimeOffset now, CancellationToken cancellationToken) =>
        jobs.PruneFinishedJobsAsync(retention.KeepLast, now - retention.MaxAge, cancellationToken);
}
