namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// Describes a data folder: <c>dataset.json</c>. The public set lives in <c>eval/</c>; a private set
/// is any folder outside the repository, or <c>eval/private/</c>, which is git-ignored (E01-S10-T05).
/// </summary>
public sealed record DataSetInfo
{
    public string Name { get; init; } = "public";

    /// <summary>
    /// Where the media files are, absolute or relative to the data folder.
    /// </summary>
    public string MediaRoot { get; init; } = "corpus";

    /// <summary>
    /// A private set skips the licence and size checks, and its reports stay in its own folder.
    /// </summary>
    public bool Private { get; init; }
}
