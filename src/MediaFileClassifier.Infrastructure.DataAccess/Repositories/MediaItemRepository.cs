using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
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
    public event EventHandler? Changed;

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
    public Task<IReadOnlyList<MediaItem>> FindByContentHashAsync(UInt128 contentHash, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems
                .Where(item => item.Hashes.ContentHash == contentHash)
                .OrderBy(item => item.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> FindByFingerprintAsync(UInt128 fingerprint, long sizeBytes, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems
                .Where(item => item.Hashes.Fingerprint == fingerprint && item.SizeBytes == sizeBytes)
                .OrderBy(item => item.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItemIdentity>>(
            async (db, token) =>
            {
                var rows = await db.MediaItems
                    .OrderBy(item => item.Id)
                    .Select(item => new { item.Id, item.Path, item.MediaType, item.SizeBytes, item.FileModifiedAt, item.Hashes.PerceptualHash })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return rows
                    .Select(row => new MediaItemIdentity(row.Id, row.Path.FileName, row.MediaType, row.SizeBytes, row.FileModifiedAt, row.PerceptualHash))
                    .ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetWithoutContentHashAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems
                .Where(item => item.Hashes.ContentHash == null)
                .OrderBy(item => item.Id)
                .Take(limit)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetByStatusAsync(MediaItemStatus status, Id<IndexedFolder>? folderId = null, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems
                .Where(item => item.Status == status && (folderId == null || item.FolderId == folderId))
                .OrderBy(item => item.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<IReadOnlyList<ExactDuplicateGroup>> GetExactDuplicatesAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<ExactDuplicateGroup>>(
            async (db, token) =>
            {
                // The content hash column is indexed, so the shared hashes are found without reading every item.
                var shared = await db.MediaItems
                    .Where(item => item.Hashes.ContentHash != null)
                    .GroupBy(item => item.Hashes.ContentHash)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                if (shared.Count == 0)
                {
                    return [];
                }

                var items = await db.MediaItems
                    .Where(item => shared.Contains(item.Hashes.ContentHash))
                    .Select(item => new { item.Id, item.Hashes.ContentHash })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return items
                    .GroupBy(item => item.ContentHash!.Value)
                    .OrderBy(group => group.Key)
                    .Select(group => new ExactDuplicateGroup(group.Key, [.. group.Select(item => item.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal)]))
                    .ToList();
            },
            cancellationToken);

    public Task<TechnicalMetadata?> GetTechnicalMetadataAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.TechnicalMetadata.SingleOrDefaultAsync(metadata => metadata.ItemId == itemId, token), cancellationToken);

    public Task<IReadOnlyList<MediaItem>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems.OrderBy(item => item.Id).Skip(offset).Take(limit).ToListAsync(token).ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<MediaItemSummary>>(
            async (db, token) =>
            {
                var rows = await (
                    from item in db.MediaItems.OrderByDescending(item => item.Id).Skip(offset).Take(limit)
                    join metadata in db.TechnicalMetadata on item.Id equals metadata.ItemId into found
                    from metadata in found.DefaultIfEmpty()
                    orderby item.Id descending
                    select new
                    {
                        item.Id,
                        item.Path,
                        item.MediaType,
                        item.SizeBytes,
                        item.FileModifiedAt,
                        item.Status,
                        item.StatusReason,
                        Duration = metadata == null ? null : metadata.Duration,
                        Warnings = metadata == null ? null : metadata.Warnings,
                    }).ToListAsync(token).ConfigureAwait(false);
                return [.. rows.Select(row => new MediaItemSummary(
                    row.Id,
                    row.Path.FileName,
                    row.Path.Extension,
                    row.MediaType,
                    row.SizeBytes,
                    row.FileModifiedAt,
                    row.Status,
                    row.StatusReason,
                    row.Duration,
                    row.Warnings?.Any(warning => warning.Code is ThumbnailWarnings.ThumbnailFailed or ThumbnailWarnings.ThumbnailFfmpegUnavailable) ?? false))];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountNewerAsync(Id<MediaItem> id, CancellationToken cancellationToken = default)
    {
        // IDs are stored as lower-case UUIDv7 text, which sorts by creation time. Id<T> has no comparison for LINQ.
        var text = id.Value.ToString();
        return connections.ReadAsync(
            (db, token) => db.Database.SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM media_item WHERE id > {text}").SingleAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // Raw SQL for the same reason as CountNewerAsync: IDs compare as UUIDv7 text.
        var upTo = newest?.Value.ToString() ?? "ffffffff-ffff-ffff-ffff-ffffffffffff";
        return connections.ReadAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) =>
            {
                var ids = await db.Database
                    .SqlQuery<string>($"SELECT id AS \"Value\" FROM media_item WHERE id <= {upTo} ORDER BY id DESC LIMIT {limit} OFFSET {offset}")
                    .ToListAsync(token).ConfigureAwait(false);
                return [.. ids.Select(Id<MediaItem>.Parse)];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<Id<MediaItem>>>(new HashSet<Id<MediaItem>>());
        }

        return connections.ReadAsync<IReadOnlySet<Id<MediaItem>>>(
            async (db, token) =>
            {
                var existing = new HashSet<Id<MediaItem>>();
                // EF sends each batch as one JSON parameter, so batches can be large.
                foreach (var batch in ids.Chunk(5000))
                {
                    var found = await db.MediaItems.Where(item => batch.Contains(item.Id)).Select(item => item.Id).ToListAsync(token).ConfigureAwait(false);
                    existing.UnionWith(found);
                }

                return existing;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.MediaItems.CountAsync(token), cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await writer.ExecuteAsync(
            (db, token) =>
            {
                db.MediaItems.Add(item);
                return db.SaveChangesAsync(token);
            },
            cancellationToken).ConfigureAwait(false);
        RaiseChanged();
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
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await writer.ExecuteAsync(
            (db, token) =>
            {
                db.MediaItems.Update(item);
                return db.SaveChangesAsync(token);
            },
            cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task UpdateAsync(MediaItem item, TechnicalMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.ItemId != item.Id)
        {
            throw new ArgumentException("The metadata belongs to another item.", nameof(metadata));
        }

        await writer.ExecuteAsync(
            async (db, token) =>
            {
                db.MediaItems.Update(item);
                if (await db.TechnicalMetadata.AnyAsync(existing => existing.ItemId == item.Id, token).ConfigureAwait(false))
                {
                    db.TechnicalMetadata.Update(metadata);
                }
                else
                {
                    db.TechnicalMetadata.Add(metadata);
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        RaiseChanged();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Id<MediaItem> id, CancellationToken cancellationToken = default)
    {
        var deleted = await writer.ExecuteAsync(
            async (db, token) => await db.MediaItems.Where(item => item.Id == id).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0,
            cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            RaiseChanged();
        }

        return deleted;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
