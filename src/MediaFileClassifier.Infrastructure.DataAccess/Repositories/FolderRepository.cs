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
    public async Task<IndexedFolder?> GetAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
    {
        await using var db = await connections.CreateContextAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await db.Folders.SingleOrDefaultAsync(folder => folder.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFolder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await connections.CreateContextAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await db.Folders.OrderBy(folder => folder.AddedAt).ThenBy(folder => folder.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

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
