using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// A job ran to its end (E15-S01-T02): its final counters and how it ended.
/// </summary>
/// <param name="Progress">The job's counters when it ended.</param>
/// <param name="State">How it ended: <see cref="ProcessingJobState.Completed"/> or <see cref="ProcessingJobState.Failed"/>
/// (every work item failed).</param>
public sealed record JobFinishedEventArgs(JobProgress Progress, ProcessingJobState State);
