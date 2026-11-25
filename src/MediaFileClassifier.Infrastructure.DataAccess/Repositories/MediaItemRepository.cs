using System.Collections.Concurrent;
using System.Reflection;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores media items. Reads use their own connection; writes go through the writer (D072).
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="logger">Logs listeners of <see cref="Changed"/> that fail.</param>
internal sealed partial class MediaItemRepository(LibraryConnectionFactory connections, LibraryWriter writer, ILogger<MediaItemRepository> logger)
    : IMediaItemRepository
{
    /// <summary>
    /// Number of items written per transaction by <see cref="AddRangeAsync"/> and by the tag operations on many items
    /// of <see cref="ItemMetadataRepository"/>. A user edit queued behind two ingesting
    /// jobs waited about 20 ms (p50) and 45 ms (max) with 100, against 170 ms and 240 ms with 500, for the same
    /// throughput (stress test of E01-S03-T04).
    /// </summary>
    public const int BatchSize = 100;

    // IDs per query when items are read by ID, well under SQLite's limit of parameters.
    private const int ReadChunkSize = 500;

    // Listeners that failed already: a later failure of the same one is logged at debug level.
    private readonly ConcurrentDictionary<MethodInfo, byte> failedListeners = new();

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

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetByFolderAsync(Id<IndexedFolder> folderId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) => await db.MediaItems.Where(item => item.FolderId == folderId).OrderBy(item => item.Id).ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<FolderItemCount>> CountByFolderAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<FolderItemCount>>(
            async (db, token) => (await db.MediaItems
                    .GroupBy(item => new { item.FolderId, item.Status })
                    .Select(group => new { group.Key.FolderId, group.Key.Status, Count = group.Count() })
                    .ToListAsync(token)
                    .ConfigureAwait(false))
                .ConvertAll(row => new FolderItemCount(row.FolderId, row.Status, row.Count)),
            cancellationToken);

    public Task<IReadOnlyList<ItemStatisticsCount>> CountByTypeAndStatusAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<ItemStatisticsCount>>(
            async (db, token) => (await db.MediaItems
                    .GroupBy(item => new { item.MediaType, item.Status })
                    .Select(group => new { group.Key.MediaType, group.Key.Status, Count = group.Count(), SizeBytes = group.Sum(item => item.SizeBytes) })
                    .ToListAsync(token)
                    .ConfigureAwait(false))
                .ConvertAll(row => new ItemStatisticsCount(row.MediaType, row.Status, row.Count, row.SizeBytes)),
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
    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        ReadSummariesAsync(null, offset, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesAsync(MediaItemFilter filter, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return ReadSummariesAsync(filter, offset, limit, cancellationToken);
    }

    private Task<IReadOnlyList<MediaItemSummary>> ReadSummariesAsync(MediaItemFilter? filter, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<MediaItemSummary>>(
            (db, token) => ToSummariesAsync(db, Filter(db, filter).OrderByDescending(item => item.Id).Skip(offset).Take(limit), token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetFilteredIdsAsync(MediaItemFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return connections.ReadAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) => await Filter(db, filter).OrderByDescending(item => item.Id).Select(item => item.Id).ToListAsync(token).ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemSummary>> GetSummariesByIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<MediaItemSummary>>([]);
        }

        return connections.ReadAsync<IReadOnlyList<MediaItemSummary>>(
            async (db, token) =>
            {
                var summaries = new List<MediaItemSummary>(ids.Count);
                foreach (var batch in ids.Chunk(5000))
                {
                    summaries.AddRange(await ToSummariesAsync(db, db.MediaItems.Where(item => batch.Contains(item.Id)), token).ConfigureAwait(false));
                }

                return summaries;
            },
            cancellationToken);
    }

    private static async Task<IReadOnlyList<MediaItemSummary>> ToSummariesAsync(LibraryDbContext db, IQueryable<MediaItem> items, CancellationToken token)
    {
        var rows = await (
            from item in items
            join metadata in db.TechnicalMetadata on item.Id equals metadata.ItemId into found
            from metadata in found.DefaultIfEmpty()
            join folder in db.Folders on item.FolderId equals folder.Id into folders
            from folder in folders.DefaultIfEmpty()
            orderby item.Id descending
            select new
            {
                item.Id,
                item.Path,
                FolderPath = folder == null ? null : folder.DisplayPath,
                FolderOffline = folder != null && folder.Status == FolderStatus.Offline,
                VolumeLabel = folder == null ? null : folder.VolumeLabel,
                item.MediaType,
                item.SizeBytes,
                item.FileModifiedAt,
                item.Status,
                item.StatusReason,
                item.IsFavorite,
                Duration = metadata == null ? null : metadata.Duration,
                Warnings = metadata == null ? null : metadata.Warnings,
                DateTaken = metadata == null ? null : metadata.DateTaken,
                Width = metadata == null ? null : metadata.Width,
                Height = metadata == null ? null : metadata.Height,
                Orientation = metadata == null ? ImageOrientation.Normal : metadata.Orientation,
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
            row.Warnings?.Any(warning => warning.Code is ThumbnailWarnings.ThumbnailFailed or ThumbnailWarnings.ThumbnailFfmpegUnavailable) ?? false,
            row.IsFavorite,
            row.DateTaken,
            row.Orientation >= ImageOrientation.Transpose ? row.Height : row.Width,
            row.Orientation >= ImageOrientation.Transpose ? row.Width : row.Height,
            FolderOf(row.FolderPath, row.Path),
            row.FolderOffline,
            row.VolumeLabel))];
    }

    private static string? FolderOf(string? folderPath, RelativePath path)
    {
        var slash = path.Value.LastIndexOf('/');
        var subfolders = slash < 0 ? string.Empty : path.Value[..slash].Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (folderPath is null)
        {
            return subfolders.Length == 0 ? null : subfolders;
        }

        return subfolders.Length == 0 ? folderPath : System.IO.Path.Join(folderPath, subfolders);
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
    public Task<IReadOnlyList<MediaItem>> GetByIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<MediaItem>>([]);
        }

        return connections.ReadAsync<IReadOnlyList<MediaItem>>(
            async (db, token) =>
            {
                var items = new List<MediaItem>(ids.Count);
                foreach (var batch in ids.Distinct().Chunk(5000))
                {
                    items.AddRange(await db.MediaItems.Where(item => batch.Contains(item.Id)).ToListAsync(token).ConfigureAwait(false));
                }

                return items;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemPath>> GetPathsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<MediaItemPath>>([]);
        }

        return connections.ReadAsync<IReadOnlyList<MediaItemPath>>(
            async (db, token) =>
            {
                var paths = new List<MediaItemPath>(ids.Count);
                foreach (var batch in ids.Chunk(5000))
                {
                    var rows = await db.MediaItems
                        .Where(item => batch.Contains(item.Id))
                        .Select(item => new { item.Id, item.FolderId, item.Path, item.Status })
                        .ToListAsync(token).ConfigureAwait(false);
                    paths.AddRange(rows.Select(row => new MediaItemPath(row.Id, row.FolderId, row.Path, row.Status)));
                }

                return paths;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.MediaItems.CountAsync(token), cancellationToken);

    /// <inheritdoc />
    public Task<int> CountNewerAsync(MediaItemFilter filter, Id<MediaItem> id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var text = id.Value.ToString();
        return connections.ReadAsync(
            (db, token) => Filter(db, filter, db.MediaItems.FromSql($"SELECT * FROM media_item WHERE id > {text}")).CountAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(MediaItemFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return connections.ReadAsync((db, token) => Filter(db, filter).CountAsync(token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string Name, int Count)>> GetSubfoldersAsync(Id<IndexedFolder> folderId, string subfolder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subfolder);

        var parent = subfolder.Replace('\\', '/').Trim('/');
        return connections.ReadAsync<IReadOnlyList<(string Name, int Count)>>(
            async (db, token) =>
            {
                var paths = await Filter(db, new MediaItemFilter { FolderId = folderId, Subfolder = parent })
                    .Select(item => item.Path)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                // The next segment of each path below the parent that has a folder after it.
                var skip = parent.Length == 0 ? 0 : parent.Length + 1;
                return [.. paths
                    .Select(path => path.Value[skip..])
                    .Where(rest => rest.Contains('/', StringComparison.Ordinal))
                    .GroupBy(rest => rest[..rest.IndexOf('/', StringComparison.Ordinal)], StringComparer.OrdinalIgnoreCase)
                    .Select(group => (group.Key, group.Count()))
                    .OrderBy(subfolder => subfolder.Key, StringComparer.CurrentCultureIgnoreCase)];
            },
            cancellationToken);
    }

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
    public async Task UpdateRangeAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var batch in items.Chunk(BatchSize))
        {
            await writer.ExecuteAsync(
                (db, token) =>
                {
                    db.MediaItems.UpdateRange(batch);
                    return db.SaveChangesAsync(token);
                },
                cancellationToken).ConfigureAwait(false);
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public async Task UpdateFilesAsync(IReadOnlyCollection<MediaItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return;
        }

        var written = await writer.ExecuteAsync<IReadOnlySet<Id<MediaItem>>>(
            async (db, token) =>
            {
                var byId = items.ToDictionary(item => item.Id);
                var stored = new List<(Id<MediaItem> Id, Id<IndexedFolder> FolderId, RelativePath Path)>(byId.Count);
                foreach (var chunk in byId.Keys.Chunk(ReadChunkSize))
                {
                    var rows = await db.MediaItems.AsNoTracking().Where(item => chunk.Contains(item.Id))
                        .Select(item => new { item.Id, item.FolderId, item.Path }).ToListAsync(token).ConfigureAwait(false);
                    stored.AddRange(rows.Select(row => (row.Id, row.FolderId, row.Path)));
                }

                // An item that moves away from a path another of the items takes is parked on a path of its own first,
                // so the unique (folder, path) index holds after every statement.
                var targets = items.ToLookup(item => (item.FolderId, item.Path));
                var parked = stored.Where(row => (byId[row.Id].FolderId, byId[row.Id].Path) != (row.FolderId, row.Path)
                        && targets[(row.FolderId, row.Path)].Any(other => other.Id != row.Id))
                    .ToList();
                if (parked.Count == 0)
                {
                    // Nothing swaps: a small set is written in this same turn of the writer.
                    if (items.Count > BatchSize)
                    {
                        return new HashSet<Id<MediaItem>>();
                    }

                    await AttachFileChangesAsync(db, items, keepExclusion: true, token).ConfigureAwait(false);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    return byId.Keys.ToHashSet();
                }

                foreach (var (id, _, _) in parked)
                {
                    var temporary = RelativePath.From($".mfc-relink/{id}");
                    await db.MediaItems.Where(item => item.Id == id)
                        .ExecuteUpdateAsync(set => set.SetProperty(item => item.Path, temporary), token).ConfigureAwait(false);
                }

                var together = parked.SelectMany(row => targets[(row.FolderId, row.Path)].Select(item => item.Id))
                    .Concat(parked.Select(row => row.Id))
                    .ToHashSet();
                await AttachFileChangesAsync(db, [.. together.Select(id => byId[id])], keepExclusion: true, token).ConfigureAwait(false);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return together;
            },
            cancellationToken).ConfigureAwait(false);
        if (written.Count > 0)
        {
            RaiseChanged();
        }

        foreach (var batch in items.Where(item => !written.Contains(item.Id)).Chunk(BatchSize))
        {
            await writer.ExecuteAsync(
                async (db, token) =>
                {
                    await AttachFileChangesAsync(db, batch, keepExclusion: true, token).ConfigureAwait(false);
                    return await db.SaveChangesAsync(token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            RaiseChanged();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> SetFavoriteAsync(IReadOnlyCollection<Id<MediaItem>> ids, bool isFavorite, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var changed = new List<Id<MediaItem>>();
        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            var done = await writer.ExecuteAsync<IReadOnlyList<Id<MediaItem>>>(
                async (db, token) =>
                {
                    var due = await db.MediaItems
                        .Where(item => batch.Contains(item.Id) && item.IsFavorite != isFavorite)
                        .Select(item => item.Id)
                        .ToListAsync(token)
                        .ConfigureAwait(false);
                    if (due.Count > 0)
                    {
                        await db.MediaItems
                            .Where(item => due.Contains(item.Id))
                            .ExecuteUpdateAsync(
                                setters => setters
                                    .SetProperty(item => item.IsFavorite, isFavorite)
                                    .SetProperty(item => item.FavoriteChangedAt, now),
                                token)
                            .ConfigureAwait(false);
                    }

                    return due;
                },
                cancellationToken).ConfigureAwait(false);
            if (done.Count > 0)
            {
                changed.AddRange(done);
                RaiseChanged();
            }
        }

        return changed;
    }

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
                // The media stage may run long on an item the user edits meanwhile (MFC-1053).
                if ((await AttachFileChangesAsync(db, [item], keepExclusion: false, token).ConfigureAwait(false)).Count == 0)
                {
                    return;
                }

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

    /// <inheritdoc />
    public async Task<bool> ReplaceWithMovedAsync(MediaItem moved, Id<MediaItem> duplicateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moved);

        var written = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var movedId = moved.Id;
                var folderId = moved.FolderId;
                var path = moved.Path;
                var isMissing = await db.MediaItems.AsNoTracking()
                    .AnyAsync(item => item.Id == movedId && item.Status == MediaItemStatus.Missing, token).ConfigureAwait(false);
                if (!isMissing
                    || await db.MediaItems.Where(item => item.Id == duplicateId && item.FolderId == folderId && item.Path == path)
                        .ExecuteDeleteAsync(token).ConfigureAwait(false) == 0)
                {
                    return false;
                }

                // Only what the move changes: a favourite, review or hash change made meanwhile isn't overwritten. The
                // item is Missing, so it has no error to clear, and its hashes stay.
                var size = moved.SizeBytes;
                var created = moved.FileCreatedAt;
                var modified = moved.FileModifiedAt;
                var status = moved.Status;
                var reason = moved.StatusReason;
                var statusChangedAt = moved.StatusChangedAt;
                await db.MediaItems.Where(item => item.Id == movedId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.FolderId, folderId)
                            .SetProperty(item => item.Path, path)
                            .SetProperty(item => item.SizeBytes, size)
                            .SetProperty(item => item.FileCreatedAt, created)
                            .SetProperty(item => item.FileModifiedAt, modified)
                            .SetProperty(item => item.Status, status)
                            .SetProperty(item => item.StatusReason, reason)
                            .SetProperty(item => item.StatusChangedAt, statusChangedAt),
                        token)
                    .ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        if (written)
        {
            RaiseChanged();
        }

        return written;
    }

    // Marks only what a stage sets as modified (MFC-1053): location, file facts, hashes and status. The user's favourite
    // and review mark are never written, so an edit made while a rescan, a watched change or the media stage held the
    // item stays. With keepExclusion, the status is left as stored when either side is Excluded: rescans and watched
    // changes never exclude or restore an item, so the user removed or restored it meanwhile (E02-S10), and a moved
    // excluded item only follows its file. The import stages do restore items, so they don't ask for it. An item
    // deleted meanwhile is skipped. Runs inside the writer, so nothing changes between the check and the write.
    // Returns the IDs of the items attached.
    private static async Task<IReadOnlySet<Id<MediaItem>>> AttachFileChangesAsync(LibraryDbContext db, IReadOnlyCollection<MediaItem> items, bool keepExclusion, CancellationToken token)
    {
        var ids = items.Select(item => item.Id).ToList();
        var stored = await db.MediaItems.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.Status })
            .ToDictionaryAsync(item => item.Id, item => item.Status, token)
            .ConfigureAwait(false);
        var attached = new HashSet<Id<MediaItem>>();
        foreach (var item in items)
        {
            if (!stored.TryGetValue(item.Id, out var status))
            {
                continue;
            }

            var entry = db.MediaItems.Update(item);
            attached.Add(item.Id);
            entry.Property(row => row.ReviewStatus).IsModified = false;
            entry.Property(row => row.ReviewStatusChangedAt).IsModified = false;
            entry.Property(row => row.IsFavorite).IsModified = false;
            entry.Property(row => row.FavoriteChangedAt).IsModified = false;
            if (keepExclusion && (status == MediaItemStatus.Excluded || item.Status == MediaItemStatus.Excluded))
            {
                entry.Property(row => row.Status).IsModified = false;
                entry.Property(row => row.StatusReason).IsModified = false;
                entry.Property(row => row.StatusChangedAt).IsModified = false;
                entry.ComplexProperty(row => row.Error).IsModified = false;
            }
        }

        return attached;
    }

    // Listeners run on the writing thread, often a job's. The items are written by now, so a listener that fails is
    // logged and the others still hear the change: one listener mustn't fail the import or rescan that wrote them
    // (MFC-1002).
    private void RaiseChanged()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (var listener in changed.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                listener(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                // E.g. the listener's work was cancelled as the library closes; there is nothing to report.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A listener that fails on every change would otherwise log an error per import batch.
                var name = listener.Method.DeclaringType?.FullName ?? listener.Method.Name;
                if (failedListeners.TryAdd(listener.Method, 0))
                {
                    LogListenerFailed(exception, name);
                }
                else
                {
                    LogListenerFailedAgain(exception, name);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of the media item changes ({Listener}) failed; later failures of it are logged at debug level")]
    private partial void LogListenerFailed(Exception exception, string listener);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A listener of the media item changes ({Listener}) failed again")]
    private partial void LogListenerFailedAgain(Exception exception, string listener);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> DeleteFromFolderAsync(Id<IndexedFolder> folderId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var deleted = await writer.ExecuteAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) =>
            {
                var ids = await db.MediaItems.Where(item => item.FolderId == folderId).OrderBy(item => item.Id).Select(item => item.Id)
                    .Take(limit).ToListAsync(token).ConfigureAwait(false);
                if (ids.Count > 0)
                {
                    await db.MediaItems.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(token).ConfigureAwait(false);
                }

                return ids;
            },
            cancellationToken).ConfigureAwait(false);
        if (deleted.Count > 0)
        {
            RaiseChanged();
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> DeleteMissingAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var deleted = await writer.ExecuteAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) =>
            {
                var ids = await db.MediaItems.Where(item => item.Status == MediaItemStatus.Missing).OrderBy(item => item.Id).Select(item => item.Id)
                    .Take(limit).ToListAsync(token).ConfigureAwait(false);
                if (ids.Count > 0)
                {
                    await db.MediaItems.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(token).ConfigureAwait(false);
                }

                return ids;
            },
            cancellationToken).ConfigureAwait(false);
        if (deleted.Count > 0)
        {
            RaiseChanged();
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> ExcludeAsync(IReadOnlyCollection<Id<MediaItem>> ids, string reason, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(reason);

        var changed = new List<Id<MediaItem>>();
        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            var done = await writer.ExecuteAsync<IReadOnlyList<Id<MediaItem>>>(
                async (db, token) =>
                {
                    // Tracked, so only the status columns are written.
                    var due = await db.MediaItems.AsTracking()
                        .Where(item => batch.Contains(item.Id) && item.Status != MediaItemStatus.Excluded)
                        .ToListAsync(token).ConfigureAwait(false);
                    foreach (var item in due)
                    {
                        item.ChangeStatus(MediaItemStatus.Excluded, now, reason);
                    }

                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                    db.ChangeTracker.Clear();
                    return [.. due.Select(item => item.Id)];
                },
                cancellationToken).ConfigureAwait(false);
            if (done.Count > 0)
            {
                changed.AddRange(done);
                RaiseChanged();
            }
        }

        return changed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> DeleteExcludedAsync(IReadOnlyCollection<Id<MediaItem>> ids, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(reason);

        var deleted = new List<Id<MediaItem>>();
        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            var done = await writer.ExecuteAsync<IReadOnlyList<Id<MediaItem>>>(
                async (db, token) =>
                {
                    var due = await db.MediaItems
                        .Where(item => batch.Contains(item.Id) && item.Status == MediaItemStatus.Excluded && item.StatusReason == reason)
                        .Select(item => item.Id)
                        .ToListAsync(token).ConfigureAwait(false);
                    if (due.Count > 0)
                    {
                        await db.MediaItems.Where(item => due.Contains(item.Id)).ExecuteDeleteAsync(token).ConfigureAwait(false);
                    }

                    return due;
                },
                cancellationToken).ConfigureAwait(false);
            if (done.Count > 0)
            {
                deleted.AddRange(done);
                RaiseChanged();
            }
        }

        return deleted;
    }

    // The items a filter shows; a null filter shows every item, the Excluded ones too.
    private static IQueryable<MediaItem> Filter(LibraryDbContext db, MediaItemFilter? filter, IQueryable<MediaItem>? items = null)
    {
        items ??= db.MediaItems;
        if (filter is null)
        {
            return items;
        }

        items = items.Where(item => item.Status != MediaItemStatus.Excluded);
        if (filter.FolderId is { } folderId)
        {
            items = items.Where(item => item.FolderId == folderId);
        }

        if (SqlLike.SubfolderPattern(filter.Subfolder) is { } pattern)
        {
            items = items.Where(item => EF.Functions.Like(LibraryDbContext.FoldCase((string)(object)item.Path), pattern, SqlLike.EscapeCharacter));
        }

        if (filter.ImportedSince is { } since)
        {
            items = items.Where(item => item.ImportedAt >= since);
        }

        if (filter.Status is { } status)
        {
            items = items.Where(item => item.Status == status);
        }

        if (filter.IsFavorite is { } favorite)
        {
            items = items.Where(item => item.IsFavorite == favorite);
        }

        return items;
    }
}
