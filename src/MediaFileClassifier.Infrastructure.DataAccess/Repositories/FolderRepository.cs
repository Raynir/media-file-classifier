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
    public async Task AddAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        await writer.ExecuteAsync(
            (db, token) =>
            {
                db.Folders.Add(folder);
                return db.SaveChangesAsync(token);
            },
            cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task UpdateAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var statusChanged = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var stored = await StoredStatusAsync(db, folder.Id, token).ConfigureAwait(false);
                db.Folders.Update(folder);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return stored is { } status && status != folder.Status;
            },
            cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        RaiseIf(statusChanged);
    }

    /// <inheritdoc />
    public async Task UpdateAvailabilityAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var statusChanged = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var stored = await StoredStatusAsync(db, folder.Id, token).ConfigureAwait(false);
                var status = folder.Status;
                var changedAt = folder.StatusChangedAt;
                var label = folder.VolumeLabel;
                var handle = folder.PlatformHandle;

                // Matched on the root too: a folder relinked while the check probed its old root keeps what the relink stored.
                var updated = await db.Folders.Where(row => row.Id == folder.Id && row.PlatformHandle == handle)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(row => row.Status, status)
                            .SetProperty(row => row.StatusChangedAt, changedAt)
                            .SetProperty(row => row.VolumeLabel, label),
                        token)
                    .ConfigureAwait(false);
                return updated > 0 && stored is { } old && old != status;
            },
            cancellationToken).ConfigureAwait(false);
        RaiseIf(statusChanged);
    }

    /// <inheritdoc />
    public async Task<int> MergeIntoAsync(IndexedFolder parent, Id<IndexedFolder> childId, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var moved = await writer.ExecuteAsync(
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
            cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return moved;
    }

    /// <inheritdoc />
    public async Task<(int Moved, bool PartialDeleted)> SplitIntoAsync(IndexedFolder folder, Id<IndexedFolder> partialId, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var prefix = relativePath.Trim('/') + "/";
        var result = await writer.ExecuteAsync(
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
            cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
    {
        var deleted = await writer.ExecuteAsync(
            async (db, token) => await db.Folders.Where(folder => folder.Id == id).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0,
            cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return deleted;
    }

    private static async Task<FolderStatus?> StoredStatusAsync(LibraryDbContext db, Id<IndexedFolder> id, CancellationToken cancellationToken) =>
        (await db.Folders.AsNoTracking().Where(row => row.Id == id).Select(row => new { row.Status })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false))?.Status;

    private void RaiseIf(bool statusChanged)
    {
        if (statusChanged)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
