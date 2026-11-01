namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// One media file of an evaluation data set: an entry of <c>manifest.json</c>.
/// </summary>
public sealed record CorpusItem
{
    public required string Id { get; init; }

    /// <summary>
    /// <c>photo</c>, <c>screenshot</c>, <c>document</c>, <c>gif</c> or <c>video</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The path relative to the media root, with forward slashes.
    /// </summary>
    public required string File { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Where the file comes from and under which licence. Empty for private data sets.
    /// </summary>
    public CorpusSource? Source { get; init; }

    public string? Sha256 { get; init; }

    public long? Bytes { get; init; }
}

/// <summary>
/// The origin and licence of a public corpus file.
/// </summary>
public sealed record CorpusSource
{
    public string? CommonsTitle { get; init; }

    public string? PageUrl { get; init; }

    public string? Author { get; init; }

    public string? Licence { get; init; }

    public string? LicenceUrl { get; init; }

    /// <summary>
    /// The generator script, for files made for this project.
    /// </summary>
    public string? Synthetic { get; init; }
}
