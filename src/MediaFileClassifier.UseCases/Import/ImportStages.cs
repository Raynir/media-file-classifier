namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// The import pipeline's job types and stage keys (E03-S07). The keys are stored with each work item, so they never change.
/// </summary>
public static class ImportStages
{
    /// <summary>
    /// Job type of a folder's import: discovery, fingerprints, metadata and thumbnails.
    /// </summary>
    public const string ImportJob = "import";

    /// <summary>
    /// Job type of a folder's full hashing, at low priority, after its items are imported.
    /// </summary>
    public const string FullHashJob = "full-hash";

    /// <summary>
    /// Finds the folder's new files and registers them as Discovered items. One work item for the whole folder.
    /// </summary>
    public const string Discover = "import.discover";

    /// <summary>
    /// Computes an item's quick fingerprint.
    /// </summary>
    public const string Fingerprint = "import.fingerprint";

    /// <summary>
    /// Extracts an item's technical metadata, makes its thumbnails, and marks it Pending analysis.
    /// </summary>
    public const string Media = "import.media";

    /// <summary>
    /// Computes an item's full content hash and perceptual hash, which find exact duplicates.
    /// </summary>
    public const string ContentHash = "hash.content";

    /// <summary>
    /// Gets an English name for each stage, for progress until the UI localizes them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Discover] = "Finding files",
        [Fingerprint] = "Fingerprinting",
        [Media] = "Reading metadata and thumbnails",
        [ContentHash] = "Hashing",
    };
}
