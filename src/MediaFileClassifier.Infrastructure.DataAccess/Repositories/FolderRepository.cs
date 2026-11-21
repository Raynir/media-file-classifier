using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
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

    /// <inheritdoc />
    public Task<int> MergeIntoAsync(IndexedFolder parent, Id<IndexedFolder> childId, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var items = await db.MediaItems.Where(item => item.FolderId == childId).ToListAsync(token).ConfigureAwait(false);
                foreach (var item in items)
                {
                    item.Relocate(parent.Id, RelativePath.From(relativePath + "/" + item.Path.Value));
                }

                db.Folders.Update(parent);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await db.Folders.Where(folder => folder.Id == childId).ExecuteDeleteAsync(token).ConfigureAwait(false);
                return items.Count;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<(int Moved, bool PartialDeleted)> SplitIntoAsync(IndexedFolder folder, Id<IndexedFolder> partialId, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var prefix = relativePath.Trim('/') + "/";
        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var items = await db.MediaItems.Where(item => item.FolderId == partialId).ToListAsync(token).ConfigureAwait(false);

                // Paths compare like the platform's folder handles, which ignore case on Windows.
                var moved = items.Where(item => item.Path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in moved)
                {
                    item.Relocate(folder.Id, RelativePath.From(item.Path.Value[prefix.Length..]));
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                var deleted = moved.Count == items.Count
                    && await db.Folders.Where(partial => partial.Id == partialId).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0;
                return (moved.Count, deleted);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) => await db.Folders.Where(folder => folder.Id == id).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0,
            cancellationToken);
}
