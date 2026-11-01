using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores indexed folders.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class FolderRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IFolderRepository
{
    /// <inheritdoc />
    public Task<IndexedFolder?> GetAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.Folders.SingleOrDefaultAsync(folder => folder.Id == id, token), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexedFolder>> GetAllAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<IndexedFolder>>(
            async (db, token) => await db.Folders.OrderBy(folder => folder.AddedAt).ThenBy(folder => folder.Id)
                .ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return writer.ExecuteAsync(
            (db, token) =>
            {
                db.Folders.Add(folder);
                return db.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return writer.ExecuteAsync(
            (db, token) =>
            {
                db.Folders.Update(folder);
                return db.SaveChangesAsync(token);
            },
            cancellationToken);
    }
}
