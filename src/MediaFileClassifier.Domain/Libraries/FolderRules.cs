namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Which files under an indexed folder belong to the library. Patterns are globs matched against
/// paths relative to the folder root, with <c>/</c> as the separator on every platform.
/// </summary>
public sealed record FolderRules
{
    /// <summary>
    /// Gets a value indicating whether subfolders are scanned.
    /// </summary>
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>
    /// Gets the patterns a file must match to be included. Empty includes every supported media file.
    /// </summary>
    public IReadOnlyList<string> IncludePatterns { get; init; } = [];

    /// <summary>
    /// Gets the patterns that exclude a file even when it matches an include pattern.
    /// </summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = [];
}
