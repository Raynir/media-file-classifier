using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the indexed folders of an open library.
/// </summary>
public interface IFolderRepository
{
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
}
