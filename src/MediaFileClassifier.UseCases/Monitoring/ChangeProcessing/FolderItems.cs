using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// A folder's items by path while a batch of changes is applied. A small batch looks paths up one by one; a large one,
/// such as a renamed folder, reads the folder's items once. Moves made during the batch are seen by later lookups.
/// </summary>
internal sealed class FolderItems
{
    /// <summary>
    /// The batch size from which the folder's items are read at once.
    /// </summary>
    public const int ReadAllFrom = 50;

    /// <summary>
    /// How many paths are looked under one by one before the folder's items are read at once: each look scans the
    /// folder's rows in the database.
    /// </summary>
    public const int ReadUnderLimit = 3;

    private readonly IOpenLibrary library;
    private readonly IndexedFolder folder;

    // A null value is a path known to have no item, e.g. one an item moved away from.
    private readonly Dictionary<string, MediaItem?> byPath = new(StringComparer.Ordinal);
    private bool readAll;
    private int readsUnder;

    private FolderItems(IOpenLibrary library, IndexedFolder folder)
    {
        this.library = library;
        this.folder = folder;
    }

    /// <summary>
    /// Starts the lookup for a batch.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="changes">How many changes the batch has.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lookup.</returns>
    public static async Task<FolderItems> CreateAsync(IOpenLibrary library, IndexedFolder folder, int changes, CancellationToken cancellationToken)
    {
        var items = new FolderItems(library, folder);
        if (changes >= ReadAllFrom)
        {
            await items.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return items;
    }

    /// <summary>
    /// Finds the item at a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public async Task<MediaItem?> FindAsync(RelativePath path, CancellationToken cancellationToken)
    {
        if (byPath.TryGetValue(path.Value, out var known) || readAll)
        {
            return known;
        }

        var item = await library.Items.FindByPathAsync(folder.Id, path, cancellationToken).ConfigureAwait(false);
        byPath[path.Value] = item;
        return item;
    }

    /// <summary>
    /// Finds the items at a path or under it, for a deleted path that may have been a folder. Unless the folder's items
    /// were read at once, only the items under the path are read (MFC-1054).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items.</returns>
    public async Task<IReadOnlyList<MediaItem>> FindUnderAsync(RelativePath path, CancellationToken cancellationToken)
    {
        if (await FindAsync(path, cancellationToken).ConfigureAwait(false) is { } item)
        {
            return [item];
        }

        var prefix = path.Value + "/";
        if (!readAll && ++readsUnder > ReadUnderLimit)
        {
            await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!readAll)
        {
            // What the batch made of a path wins over the stored item: one moved during the batch is known by its ID.
            var known = byPath.Values.OfType<MediaItem>().Select(other => other.Id).ToHashSet();
            foreach (var stored in await library.Items.GetUnderAsync(folder.Id, path, cancellationToken).ConfigureAwait(false))
            {
                if (!known.Contains(stored.Id))
                {
                    byPath.TryAdd(stored.Path.Value, stored);
                }
            }
        }

        return [.. byPath.Values.OfType<MediaItem>().Where(other => other.Path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Records that an item moved to another path of the folder; <see cref="MediaItem.Relocate"/> is the caller's.
    /// </summary>
    /// <param name="item">The item, at its new path.</param>
    /// <param name="oldPath">Its old path.</param>
    public void Moved(MediaItem item, RelativePath oldPath)
    {
        byPath[oldPath.Value] = null;
        byPath[item.Path.Value] = item;
    }

    private async Task ReadAllAsync(CancellationToken cancellationToken)
    {
        if (readAll)
        {
            return;
        }

        // Paths already known keep what the batch made of them.
        foreach (var item in await library.Items.GetByFolderAsync(folder.Id, cancellationToken).ConfigureAwait(false))
        {
            byPath.TryAdd(item.Path.Value, item);
        }

        readAll = true;
    }
}
