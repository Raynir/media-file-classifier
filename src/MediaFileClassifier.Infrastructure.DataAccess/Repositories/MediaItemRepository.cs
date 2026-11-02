using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores media items. Reads use their own connection; writes go through the writer (D072).
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class MediaItemRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IMediaItemRepository
{
    /// <summary>
    /// Number of items written per transaction by <see cref="AddRangeAsync"/> and by the tag operations on many items
    /// of <see cref="ItemMetadataRepository"/>. A user edit queued behind two ingesting
    /// jobs waited about 20 ms (p50) and 45 ms (max) with 100, against 170 ms and 240 ms with 500, for the same
    /// throughput (stress test of E01-S03-T04).
    /// </summary>
    public const int BatchSize = 100;

    /// <inheritdoc />
    public Task<MediaItem?> GetAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.MediaItems.SingleOrDefaultAsync(item => item.Id == id, token), cancellationToken);

    /// <inheritdoc />
    public Task<MediaItem?> FindByPathAsync(Id<IndexedFolder> folderId, RelativePath path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        return connections.ReadAsync(
            (db, token) => db.MediaItems.SingleOrDefaultAsync(item => item.FolderId == folderId && item.Path == path, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemFileState>> GetFileStatesAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItemFileState>>(
            async (db, token) => await db.MediaItems
                .Where(item => item.FolderId == folderId)
                .Select(item => new MediaItemFileState(item.Path, item.SizeBytes, item.FileModifiedAt))
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems.OrderBy(item => item.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.MediaItems.CountAsync(token), cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return writer.ExecuteAsync(
            (db, token) =>
            {
                db.MediaItems.Add(item);
                return db.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var batch in items.Chunk(BatchSize))
        {
            await writer.ExecuteAsync(
                (db, token) =>
                {
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    db.MediaItems.AddRange(batch);
                    return db.SaveChangesAsync(token);
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task UpdateAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return writer.ExecuteAsync(
            (db, token) =>
            {
                db.MediaItems.Update(item);
                return db.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Id<MediaItem> id, CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) => await db.MediaItems.Where(item => item.Id == id).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0,
            cancellationToken);
}
