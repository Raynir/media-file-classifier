namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Which files under an indexed folder belong to the library. Patterns are globs matched against
/// paths relative to the folder root, with <c>/</c> as the separator on every platform.
/// </summary>
/// <remarks>
/// Patterns ignore case. <c>*</c> matches any characters within one path segment, <c>?</c> one character, and a
/// <c>**</c> segment any number of segments. A pattern without <c>/</c> matches a file or folder name at any depth,
/// such as <c>*.tmp</c>; a pattern with <c>/</c> matches from the folder root, such as <c>2024/raw</c>. A pattern that
/// matches a folder applies to everything inside it.
/// </remarks>
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

    /// <summary>
    /// Gets a value indicating whether hidden and system files and folders are included. They're skipped by default.
    /// </summary>
    public bool IncludeHidden { get; init; }

    /// <summary>
    /// Gets a value indicating whether cloud placeholders, such as OneDrive files that are only online, are imported.
    /// They're skipped by default, because reading one downloads it.
    /// </summary>
    public bool IncludeCloudPlaceholders { get; init; }
}
