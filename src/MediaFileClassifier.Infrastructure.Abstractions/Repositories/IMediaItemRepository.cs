using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the media items of an open library.
/// </summary>
public interface IMediaItemRepository
{
    /// <summary>
    /// Gets an item.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <c>null</c> if there's none with the ID.</returns>
    Task<MediaItem?> GetAsync(Id<MediaItem> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the item at a path in a folder.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="path">Path relative to the folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <c>null</c> if there's none at the path.</returns>
    Task<MediaItem?> FindByPathAsync(Id<IndexedFolder> folderId, RelativePath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of items in ID order, which follows creation time to the millisecond (UUIDv7).
    /// </summary>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items.</returns>
    Task<IReadOnlyList<MediaItem>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an item.
    /// </summary>
    /// <param name="item">The new item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the item is stored.</returns>
    Task AddAsync(MediaItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds many items, e.g. during ingestion. They're written in batches, each its own short write, so other writes
    /// such as the user's edits get their turn between batches (D072).
    /// </summary>
    /// <param name="items">The new items.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>A task that completes when every item is stored.</returns>
    Task AddRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the changes made to an item, e.g. by <see cref="MediaItem.ChangeStatus"/>.
    /// </summary>
    /// <param name="item">The changed item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are stored.</returns>
    Task UpdateAsync(MediaItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item with everything that belongs to it: metadata, runs, tags, keyframes, embeddings and
    /// collection memberships. Thumbnails on disk are the caller's to delete.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the item existed.</returns>
    Task<bool> DeleteAsync(Id<MediaItem> id, CancellationToken cancellationToken = default);
}
