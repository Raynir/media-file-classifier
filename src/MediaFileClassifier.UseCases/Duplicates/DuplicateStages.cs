namespace MediaFileClassifier.UseCases.Duplicates;

/// <summary>
/// Job and stage names of duplicate detection (E12-S01-T03).
/// </summary>
public static class DuplicateStages
{
    /// <summary>
    /// The job type of a full detection pass.
    /// </summary>
    public const string DetectJob = "duplicates.detect";

    /// <summary>
    /// Reconciles one batch of queued content hashes (<see cref="DuplicateGroupMaintainer.BatchSize"/>).
    /// </summary>
    public const string Reconcile = "duplicates.reconcile";
}
