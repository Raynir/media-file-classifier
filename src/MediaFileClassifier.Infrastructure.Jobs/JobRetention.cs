namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// How long finished jobs are kept (E05-S03-T04): the newest ones, and the ones that finished recently enough. A
/// finished job that is over either limit is deleted with its work items. Jobs that haven't finished are never deleted.
/// </summary>
/// <param name="KeepLast">The most finished jobs to keep, the ones that finished last.</param>
/// <param name="MaxAge">How long after finishing a job is kept.</param>
public sealed record JobRetention(int KeepLast, TimeSpan MaxAge)
{
    /// <summary>
    /// Gets the default: the last 50 finished jobs, none older than 30 days.
    /// </summary>
    public static JobRetention Default { get; } = new(50, TimeSpan.FromDays(30));
}
