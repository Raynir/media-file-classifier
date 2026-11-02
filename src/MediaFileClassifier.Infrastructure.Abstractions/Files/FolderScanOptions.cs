namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// What <see cref="IMediaFileSource.ScanAsync"/> lists.
/// </summary>
public sealed record FolderScanOptions
{
    /// <summary>
    /// Gets a value indicating whether subfolders are scanned.
    /// </summary>
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether hidden and system files and folders are listed. They're skipped by default.
    /// </summary>
    public bool IncludeHidden { get; init; }

    /// <summary>
    /// Gets the check that decides whether a subfolder is entered, given its relative path with <c>/</c> separators.
    /// <see langword="null"/> enters every subfolder. Use it to prune excluded subfolders without listing them.
    /// </summary>
    public Func<string, bool>? ShouldEnterFolder { get; init; }
}
