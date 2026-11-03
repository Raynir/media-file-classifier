using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the media items of an open library.
/// </summary>
public interface IMediaItemRepository
{
    /// <summary>
    /// Raised after items were added, changed or deleted, e.g. so the media browser reloads (E14-S01-T02). It's
    /// raised on the writing thread, once per write or batch.
    /// </summary>
    event EventHandler? Changed;

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
    /// Gets the path, size and modification time of every item in a folder, without loading the items, so discovery
    /// can skip unchanged files (E03-S02-T03).
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facts, in no particular order.</returns>
    Task<IReadOnlyList<MediaItemFileState>> GetFileStatesAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the items with a full content hash, e.g. byte-identical copies (E01-S06, E12).
    /// </summary>
    /// <param name="contentHash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in ID order.</returns>
    Task<IReadOnlyList<MediaItem>> FindByContentHashAsync(UInt128 contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the items with a quick fingerprint and size, e.g. a moved or renamed file (E01-S06).
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="sizeBytes">The file size in bytes; a fingerprint is only meaningful with it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in ID order.</returns>
    Task<IReadOnlyList<MediaItem>> FindByFingerprintAsync(UInt128 fingerprint, long sizeBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the identity facts of every item, for file matching to compare in memory (E01-S06-T05).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facts, in ID order.</returns>
    Task<IReadOnlyList<MediaItemIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets items whose full content hash isn't computed yet, for the background hashing job (E01-S06-T02).
    /// </summary>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in ID order.</returns>
    Task<IReadOnlyList<MediaItem>> GetWithoutContentHashAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the items with a status, e.g. the ones an import still has to process, or the failed ones to retry (E03-S07).
    /// </summary>
    /// <param name="status">Status to look for.</param>
    /// <param name="folderId">Folder to look in, or <c>null</c> for the whole library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in ID order.</returns>
    Task<IReadOnlyList<MediaItem>> GetByStatusAsync(MediaItemStatus status, Id<IndexedFolder>? folderId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every item of a folder, whatever its status, e.g. to apply changed folder rules (E02-S03-T03).
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, in ID order.</returns>
    Task<IReadOnlyList<MediaItem>> GetByFolderAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items of each folder by status (E02-S03-T06). Folders without items aren't listed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One count per folder and status.</returns>
    Task<IReadOnlyList<FolderItemCount>> CountByFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the exact duplicate candidates: groups of items that share a full content hash (E03-S07-T06). Duplicate
    /// review and resolution build on them (E12).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups of two or more items, in content hash order, each with its items in ID order.</returns>
    Task<IReadOnlyList<ExactDuplicateGroup>> GetExactDuplicatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an item's technical metadata (E03-S03).
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata, or <c>null</c> if it isn't extracted yet.</returns>
    Task<TechnicalMetadata?> GetTechnicalMetadataAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of items in ID order, which follows creation time to the millisecond (UUIDv7).
    /// </summary>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items.</returns>
    Task<IReadOnlyList<MediaItem>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of tile summaries, newest first (E14-S01-T02). Newest means the latest ID, which follows the time
    /// the item was added (UUIDv7).
    /// </summary>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summaries.</returns>
    Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items added after an item, which is its position in the newest-first order of
    /// <see cref="GetSummariesAsync(int, int, CancellationToken)"/>. It still works after the item was deleted.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of newer items.</returns>
    Task<int> CountNewerAsync(Id<MediaItem> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of tile summaries of the items a filter shows, newest first, as <see cref="GetSummariesAsync(int, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="filter">Which items to show (E13-S02).</param>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summaries.</returns>
    Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(MediaItemFilter filter, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items a filter shows that were added after an item: its position in
    /// <see cref="GetSummariesAsync(MediaItemFilter, int, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="filter">Which items to show.</param>
    /// <param name="id">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of newer items.</returns>
    Task<int> CountNewerAsync(MediaItemFilter filter, Id<MediaItem> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the items a filter shows.
    /// </summary>
    /// <param name="filter">Which items to show.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items.</returns>
    Task<int> CountAsync(MediaItemFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the subfolders of a folder's subfolder that hold items, from the items' paths, with the number of items
    /// in each, the ones inside it included (E13-S02-T03). Excluded items don't count.
    /// </summary>
    /// <param name="folderId">The indexed folder.</param>
    /// <param name="subfolder">The subfolder whose subfolders to list, with <c>/</c> separators; empty for the folder itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subfolders' names and item counts, by name.</returns>
    Task<IReadOnlyList<(string Name, int Count)>> GetSubfoldersAsync(Id<IndexedFolder> folderId, string subfolder, CancellationToken cancellationToken = default);

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
    /// Stores the changes made to many items, in batches like <see cref="AddRangeAsync"/>.
    /// </summary>
    /// <param name="items">The changed items.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>A task that completes when every change is stored.</returns>
    Task UpdateRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the changes made to an item together with its technical metadata, in one transaction, replacing any
    /// metadata stored before.
    /// </summary>
    /// <param name="item">The changed item.</param>
    /// <param name="metadata">The item's technical metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when both are stored.</returns>
    Task UpdateAsync(MediaItem item, TechnicalMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item with everything that belongs to it: metadata, runs, tags, keyframes, embeddings and
    /// collection memberships. Thumbnails on disk are the caller's to delete.
    /// </summary>
    /// <param name="id">Item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the item existed.</returns>
    Task<bool> DeleteAsync(Id<MediaItem> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes up to <paramref name="limit"/> items of a folder, as <see cref="DeleteAsync"/> does, in one short write.
    /// Removing a folder calls it until nothing is left (E02-S03-T04).
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="limit">The most items to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deleted items, so the caller can delete their thumbnails; empty when the folder has none left.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> DeleteFromFolderAsync(Id<IndexedFolder> folderId, int limit, CancellationToken cancellationToken = default);
}
