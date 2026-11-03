namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// The progress of all unfinished jobs of the open library (E05-S02-T01), for the status bar.
/// </summary>
/// <param name="Jobs">The unfinished jobs, oldest first.</param>
/// <param name="TimeRemaining">Estimated time until all pending work is done, or <see langword="null"/> while there's
/// too little data or a job's total still grows.</param>
public sealed record ProgressSummary(IReadOnlyList<JobProgress> Jobs, TimeSpan? TimeRemaining)
{
    /// <summary>
    /// Gets the summary when nothing is to do.
    /// </summary>
    public static ProgressSummary Empty { get; } = new([], null);

    /// <summary>
    /// Gets the number of unfinished jobs.
    /// </summary>
    public int ActiveJobCount => Jobs.Count;

    /// <summary>
    /// Gets the work items still queued or running, in all jobs.
    /// </summary>
    public int PendingItems => Jobs.Sum(job => job.Pending);

    /// <summary>
    /// Gets the work items counted in all jobs.
    /// </summary>
    public int Total => Jobs.Sum(job => job.Stages.Sum(stage => stage.Total));

    /// <summary>
    /// Gets the work items finished, completed or failed, in all jobs.
    /// </summary>
    public int Finished => Total - PendingItems;
}
