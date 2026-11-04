namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Which files under an indexed folder belong to the library. Patterns are globs matched against
/// paths relative to the folder root, with <c>/</c> as the separator on every platform.
/// </summary>
/// <remarks>
/// Patterns ignore case. <c>*</c> matches any characters within one path segment, <c>?</c> one character, and a
/// <c>**</c> segment any number of segments. A pattern without <c>/</c> matches a file or folder name at any depth,
/// such as <c>*.tmp</c>; a pattern with <c>/</c> matches from the folder root, such as <c>2024/raw</c> or <c>/raw</c>.
/// A pattern that matches a folder applies to everything inside it. Excluded subfolders the user picks are stored as
/// root-anchored patterns (<see cref="ForSubfolder"/>).
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

    /// <summary>
    /// Gets the root-anchored pattern for a subfolder, as stored for an excluded subfolder (E02-S03-T03).
    /// </summary>
    /// <param name="relativePath">The subfolder's path relative to the folder root.</param>
    /// <returns>The pattern, such as <c>/2024/raw</c>.</returns>
    public static string ForSubfolder(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return "/" + relativePath.Replace('\\', '/').Trim('/');
    }

    /// <summary>
    /// Gets the exclude patterns rewritten for a parent folder this folder is inside, so they exclude the same files
    /// once the folder is merged into the parent (E02-S03-T02).
    /// </summary>
    /// <param name="relativePath">This folder's path relative to the parent.</param>
    /// <returns>The patterns, anchored at this folder's place in the parent.</returns>
    public IReadOnlyList<string> ExcludePatternsUnder(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var prefix = ForSubfolder(relativePath);
        return
        [
            .. ExcludePatterns
                .Select(pattern => pattern.Trim().Replace('\\', '/'))
                .Where(pattern => pattern.Trim('/').Length > 0)
                .Select(pattern => pattern.Contains('/', StringComparison.Ordinal)
                    ? prefix + "/" + pattern.TrimStart('/')
                    : prefix + "/**/" + pattern),
        ];
    }
}
