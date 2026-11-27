using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;

namespace MediaFileClassifier.UseCases.Duplicates.Resolution;

/// <summary>
/// A duplicate member's file: the path shown in the dialogs and the log, and the full path the trash gets.
/// </summary>
/// <param name="ItemId">The member.</param>
/// <param name="DisplayPath">The path to show: the full path, or the folder's name and the relative path where folders
/// aren't paths (Android).</param>
/// <param name="FullPath">The full path, or <see langword="null"/> where folders aren't paths; such a file can't go to
/// the trash.</param>
public sealed record DuplicateMemberPath(Id<MediaItem> ItemId, string DisplayPath, string? FullPath);

/// <summary>
/// Finds the files of duplicate members (E12-S04).
/// </summary>
public static class DuplicateMemberPaths
{
    /// <summary>
    /// Gets the files of items, in the order given. Items the library doesn't know are left out.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The files by item.</returns>
    public static async Task<IReadOnlyDictionary<Id<MediaItem>, DuplicateMemberPath>> GetAsync(
        IOpenLibrary library,
        IReadOnlyList<Id<MediaItem>> itemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(itemIds);

        var paths = (await library.Items.GetPathsAsync(itemIds, cancellationToken).ConfigureAwait(false)).ToDictionary(path => path.Id);
        var folders = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(folder => folder.Id);
        var result = new Dictionary<Id<MediaItem>, DuplicateMemberPath>();
        foreach (var id in itemIds)
        {
            if (!paths.TryGetValue(id, out var path) || !folders.TryGetValue(path.FolderId, out var folder))
            {
                continue;
            }

            var location = GetItemLocationsQueryHandler.Locate(id, path.Path, path.Status, folder);
            result[id] = new DuplicateMemberPath(id, location.FullPath ?? $"{folder.DisplayPath}/{path.Path.Value}", location.FullPath);
        }

        return result;
    }
}
