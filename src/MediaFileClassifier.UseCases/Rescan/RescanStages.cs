namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// The rescan's job type and stage keys (E02-S04). They're stored with each job and work item, so they never change.
/// New files go on through the import's stages (<see cref="Import.ImportStages"/>) in the same job.
/// </summary>
public static class RescanStages
{
    /// <summary>
    /// The job type. A job with a folder rescans that folder; one without rescans every folder of the library.
    /// </summary>
    public const string RescanJob = "rescan";

    /// <summary>
    /// Lists the folders, reconciles them with the library, and applies the result.
    /// </summary>
    public const string Reconcile = "rescan.reconcile";

    /// <summary>
    /// Fingerprints a modified or returning file and decides whether its content changed (E02-S04-T03).
    /// </summary>
    public const string Check = "rescan.check";

    /// <summary>
    /// Refreshes a changed file's technical metadata and thumbnails, and queues its hashing and re-analysis (E02-S04-T03).
    /// </summary>
    public const string Media = "rescan.media";

    /// <summary>
    /// Gets the stages' names for the progress view.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Reconcile] = "Comparing folders with the library",
        [Check] = "Checking changed files",
        [Media] = "Refreshing metadata and thumbnails",
    };
}
