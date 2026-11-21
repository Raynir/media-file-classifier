using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the indexed folders of an open library.
/// </summary>
public interface IFolderRepository
{
    /// <summary>
    /// Raised after a write changed the stored status of a folder (Online or Offline, E14-S07-T04), whichever writer it
    /// came from: an availability check, discovery, a rescan or an import. It's raised on the writing thread, after the
    /// write is stored.
    /// </summary>
    event EventHandler? StatusChanged;

    /// <summary>
    /// Gets a folder.
    /// </summary>
    /// <param name="id">Folder ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folder, or <c>null</c> if there's none with the ID.</returns>
    Task<IndexedFolder?> GetAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every folder, in the order they were added.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folders.</returns>
    Task<IReadOnlyList<IndexedFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a folder.
    /// </summary>
    /// <param name="folder">The new folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the folder is stored.</returns>
    Task AddAsync(IndexedFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the changes made to a folder, e.g. by <see cref="IndexedFolder.Relink"/>.
    /// </summary>
    /// <param name="folder">The changed folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are stored.</returns>
    Task UpdateAsync(IndexedFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores only a folder's availability: its status, when that changed and its volume label (E14-S07-T04). Every
    /// writer that only changes a folder's status uses it (availability checks, discovery, rescans, imports), because
    /// each holds the folder for as long as its probes or scan take, so storing the whole row could undo a relink or a
    /// rule change made meanwhile. Nothing is stored if the folder was relinked to another root since it was read: what
    /// was found applies to the old root.
    /// </summary>
    /// <param name="folder">The folder, with its new status and label.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the availability is stored.</returns>
    Task UpdateAvailabilityAsync(IndexedFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a folder into a parent folder that contains it (E02-S03-T02), in one transaction: the child's items move
    /// to the parent with their paths prefixed by <paramref name="relativePath"/>, keeping their IDs and metadata, the
    /// parent's changes are stored, and the child is deleted.
    /// </summary>
    /// <param name="parent">The parent, already stored, with any changed rules.</param>
    /// <param name="childId">The folder inside it.</param>
    /// <param name="relativePath">The child's path relative to the parent, with <c>/</c> separators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items moved.</returns>
    Task<int> MergeIntoAsync(IndexedFolder parent, Id<IndexedFolder> childId, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the items under a subfolder of a partial folder to a folder added for that subfolder (E03-S05), in one
    /// transaction: their paths lose the <paramref name="relativePath"/> prefix, they keep their IDs and metadata, and
    /// the partial folder is deleted if it has no items left.
    /// </summary>
    /// <param name="folder">The new folder, already stored.</param>
    /// <param name="partialId">The partial folder that contains it.</param>
    /// <param name="relativePath">The new folder's path relative to the partial folder, with <c>/</c> separators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items moved, and whether the partial folder was deleted.</returns>
    Task<(int Moved, bool PartialDeleted)> SplitIntoAsync(IndexedFolder folder, Id<IndexedFolder> partialId, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a folder that has no items left (E02-S03-T04).
    /// </summary>
    /// <param name="id">Folder ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the folder existed.</returns>
    Task<bool> DeleteAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default);
}
