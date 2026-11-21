using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.UseCases.Exclusions;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// One folder's items compared with its snapshot, by path alone (E02-S04-T01). Pure, so E04 can feed it the same way.
/// Moves aren't known here: a moved file shows as one <see cref="New"/> file and one <see cref="Disappeared"/> item until
/// <see cref="MoveDetector"/> pairs them.
/// </summary>
public sealed record FolderComparison
{
    /// <summary>
    /// Gets the new files, as unstored Discovered items.
    /// </summary>
    public IReadOnlyList<MediaItem> New { get; init; } = [];

    /// <summary>
    /// Gets the items that are as stored, or that can't be checked.
    /// </summary>
    public IReadOnlyList<MediaItem> Unchanged { get; init; } = [];

    /// <summary>
    /// Gets the items whose file changed or came back.
    /// </summary>
    public IReadOnlyList<ModifiedFile> Modified { get; init; } = [];

    /// <summary>
    /// Gets the items whose file isn't at its path, including those already Missing.
    /// </summary>
    public IReadOnlyList<MediaItem> Disappeared { get; init; } = [];

    /// <summary>
    /// Gets the items the user removed from the library whose file isn't at its path (E02-S10-T02). They stay
    /// Excluded, but a rescan can pair them with a moved file, so the file isn't imported again.
    /// </summary>
    public IReadOnlyList<MediaItem> ExcludedGone { get; init; } = [];

    /// <summary>
    /// Compares a folder's items with what's on disk now.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="items">The folder's items.</param>
    /// <param name="snapshot">The folder's snapshot.</param>
    /// <returns>The comparison.</returns>
    public static FolderComparison Compare(IndexedFolder folder, IReadOnlyList<MediaItem> items, FolderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(snapshot);

        var files = snapshot.KnownFiles.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        string[] unlisted = [.. snapshot.Result.UnreadableFolders, .. snapshot.Result.SkippedLinks];
        var unchanged = new List<MediaItem>();
        var modified = new List<ModifiedFile>();
        var disappeared = new List<MediaItem>();
        var excludedGone = new List<MediaItem>();
        foreach (var item in items)
        {
            // Rule-excluded files aren't listed at all, and user-excluded ones aren't tracked (D187, D024), except that
            // a user-removed item whose file is gone can still be paired with the moved file.
            if (item.Status == MediaItemStatus.Excluded)
            {
                if (LibraryExclusions.IsRemovedByUser(item)
                    && !files.ContainsKey(item.Path.Value)
                    && !unlisted.Any(path => IsUnder(item.Path.Value, path)))
                {
                    excludedGone.Add(item);
                }

                continue;
            }

            if (files.TryGetValue(item.Path.Value, out var file))
            {
                if (item.Status == MediaItemStatus.Missing)
                {
                    modified.Add(new ModifiedFile(item, file));
                }
                else if (IsSame(item, file) || (file.IsCloudPlaceholder && !folder.Rules.IncludeCloudPlaceholders))
                {
                    // Reading a placeholder downloads it, so a changed one is left until it's kept on the device.
                    unchanged.Add(item);
                }
                else
                {
                    modified.Add(new ModifiedFile(item, file));
                }
            }
            else if (unlisted.Any(path => IsUnder(item.Path.Value, path)))
            {
                unchanged.Add(item);
            }
            else
            {
                disappeared.Add(item);
            }
        }

        return new FolderComparison { New = snapshot.NewFiles, Unchanged = unchanged, Modified = modified, Disappeared = disappeared, ExcludedGone = excludedGone };
    }

    /// <summary>
    /// Tells whether a file has the item's stored size and modification time. Times are stored as Unix milliseconds
    /// (D071), so the file's time is cut to match.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="file">The file.</param>
    /// <returns>Whether both match.</returns>
    public static bool IsSame(MediaItem item, MediaFileStat file)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(file);

        return item.SizeBytes == file.Length
            && item.FileModifiedAt == DateTimeOffset.FromUnixTimeMilliseconds(file.LastModifiedUtc.ToUnixTimeMilliseconds());
    }

    private static bool IsUnder(string path, string folder) =>
        folder.Length == 0 || path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal);
}
