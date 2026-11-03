using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// The progress of one unfinished job (E05-S02-T01), derived from its work items, so any job type reports it without
/// calling a progress API.
/// </summary>
/// <param name="JobId">The job.</param>
/// <param name="Type">Job type key, e.g. <c>import</c>.</param>
/// <param name="Description">Description of the job.</param>
/// <param name="Stages">Counters per stage, in the order the stages first appeared.</param>
/// <param name="TimeRemaining">Estimated time until the job's pending work is done, or <see langword="null"/> while
/// there's too little data or <see cref="IsTotalGrowing"/>.</param>
public sealed record JobProgress(
    Id<ProcessingJob> JobId,
    string Type,
    string Description,
    IReadOnlyList<StageProgress> Stages,
    TimeSpan? TimeRemaining)
{
    /// <summary>
    /// Gets a value indicating whether the total still grows: a whole-target work item, e.g. discovery, is queued or
    /// running and may add more work.
    /// </summary>
    public bool IsTotalGrowing => Stages.Any(stage => stage.IsWholeTarget && stage.Pending > 0);

    /// <summary>
    /// Gets the stage to show: the per-item stage with the most pending work, i.e. the one furthest behind, or a
    /// whole-target stage when no per-item work is pending. <see langword="null"/> when nothing is pending.
    /// </summary>
    public StageProgress? CurrentStage =>
        Stages.Where(stage => !stage.IsWholeTarget && stage.Pending > 0).MaxBy(stage => stage.Pending)
            ?? Stages.FirstOrDefault(stage => stage.Pending > 0);

    /// <summary>
    /// Gets the work items still queued or running, in all stages.
    /// </summary>
    public int Pending => Stages.Sum(stage => stage.Pending);

    /// <summary>
    /// Gets the failed work items, in all stages.
    /// </summary>
    public int Failed => Stages.Sum(stage => stage.Failed);
}
