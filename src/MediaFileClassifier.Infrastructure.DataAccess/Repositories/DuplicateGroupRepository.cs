using System.Globalization;
using System.Text.Json;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores duplicate groups (E12-S01). The <c>DuplicateGroups</c> migration's triggers queue changed content hashes in
/// <c>duplicate_dirty_hash</c>; <see cref="ReconcileAsync(int, DateTimeOffset, CancellationToken)"/> works the queue
/// off on the writer, so it never races an import or another write (D072).
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="logger">Logs listeners of <see cref="Changed"/> that fail.</param>
internal sealed partial class DuplicateGroupRepository(LibraryConnectionFactory connections, LibraryWriter writer, ILogger<DuplicateGroupRepository> logger)
    : IDuplicateGroupRepository
{
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<DuplicateGroup?> GetAsync(Id<DuplicateGroup> id, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => db.DuplicateGroups.SingleOrDefaultAsync(group => group.Id == id, token), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DuplicateGroup>> GetGroupsAsync(DuplicateGroupState? state, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync<IReadOnlyList<DuplicateGroup>>(
            async (db, token) => await Filtered(db, state)
                .OrderBy(group => group.CreatedAt).ThenBy(group => group.Id)
                .Skip(offset).Take(limit)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(DuplicateGroupState? state, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => Filtered(db, state).CountAsync(token), cancellationToken);

    /// <inheritdoc />
    public Task<DuplicateStatistics> GetOpenStatisticsAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            async (db, token) =>
            {
                // State 0 is Open. One pass over the Open groups' members, through the state and member item indexes.
                var row = await db.Database
                    .SqlQuery<OpenStatisticsRow>(
                        $"""
                        SELECT count(*) AS "OpenGroups", COALESCE(SUM("wasted"), 0) AS "WastedBytes"
                        FROM (
                            SELECT SUM(i."size_bytes") - MAX(i."size_bytes") AS "wasted"
                            FROM "duplicate_group" g
                            JOIN "duplicate_group_member" m ON m."group_id" = g."id"
                            JOIN "media_item" i ON i."id" = m."item_id"
                            WHERE g."state" = 0
                            GROUP BY g."id")
                        """)
                    .SingleAsync(token)
                    .ConfigureAwait(false);
                return new DuplicateStatistics(row.OpenGroups, row.WastedBytes);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DuplicateGroupMember>> GetMembersAsync(Id<DuplicateGroup> groupId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<DuplicateGroupMember>>(
            async (db, token) => await db.DuplicateGroupMembers
                .Where(member => member.GroupId == groupId)
                .OrderBy(member => member.ItemId)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ChangeStateAsync(Id<DuplicateGroup> id, DuplicateGroupState state, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var changed = await writer.ExecuteAsync(
            async (db, token) =>
            {
                var group = await db.DuplicateGroups.SingleOrDefaultAsync(group => group.Id == id, token).ConfigureAwait(false);
                if (group is null || !group.ChangeState(state, now))
                {
                    return false;
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            RaiseChanged();
        }

        return changed;
    }

    /// <inheritdoc />
    public async Task<DuplicateReconcileResult> ReconcileAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var result = await writer.ExecuteAsync((db, token) => ReconcileAsync(db, limit, now, token), cancellationToken).ConfigureAwait(false);
        if (result.ChangedGroups)
        {
            RaiseChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public Task<int> QueueAllAsync(CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                // Status 4 is Missing and 5 Excluded, as in the migration's triggers.
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash")
                    SELECT "hashes_content_hash" FROM "media_item"
                    WHERE "hashes_content_hash" IS NOT NULL AND "status" NOT IN (4, 5)
                    GROUP BY "hashes_content_hash" HAVING count(*) > 1
                    """,
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash")
                    SELECT "content_hash" FROM "duplicate_group" WHERE "content_hash" IS NOT NULL
                    """,
                    token).ConfigureAwait(false);
                return await CountQueuedAsync(db, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CountQueuedAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync(CountQueuedAsync, cancellationToken);

    private static Task<int> CountQueuedAsync(LibraryDbContext db, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<int>("""SELECT count(*) AS "Value" FROM "duplicate_dirty_hash" """).SingleAsync(cancellationToken);

    private static IQueryable<DuplicateGroup> Filtered(LibraryDbContext db, DuplicateGroupState? state) =>
        state is { } only ? db.DuplicateGroups.Where(group => group.State == only) : db.DuplicateGroups;

    private static async Task<DuplicateReconcileResult> ReconcileAsync(LibraryDbContext db, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var texts = await db.Database
            .SqlQuery<string>($"""SELECT "content_hash" AS "Value" FROM "duplicate_dirty_hash" LIMIT {limit}""")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (texts.Count == 0)
        {
            return DuplicateReconcileResult.Empty;
        }

        // A row that isn't a hash can't name a group; it's dropped with the others, so it never blocks the queue.
        List<UInt128?> hashes = [.. texts
            .Select(text => UInt128.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hash) ? hash : (UInt128?)null)
            .Where(hash => hash is not null)];

        // Missing and Excluded items aren't members: the file is gone, or the user removed it from the library.
        var items = await db.MediaItems
            .Where(item => hashes.Contains(item.Hashes.ContentHash)
                && item.Status != MediaItemStatus.Missing
                && item.Status != MediaItemStatus.Excluded)
            .Select(item => new { item.Id, item.Hashes.ContentHash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var groups = await db.DuplicateGroups
            .Where(group => group.Type == DuplicateGroupType.Exact && hashes.Contains(group.ContentHash))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<Id<DuplicateGroup>> groupIds = [.. groups.Select(group => group.Id)];
        var members = (await db.DuplicateGroupMembers
                .Where(member => groupIds.Contains(member.GroupId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToLookup(member => member.GroupId);

        var itemsByHash = items.ToLookup(item => item.ContentHash!.Value, item => item.Id);
        var groupByHash = groups.ToDictionary(group => group.ContentHash!.Value);
        int created = 0, changed = 0, reopened = 0, deleted = 0;
        foreach (var hash in hashes.Select(hash => hash!.Value))
        {
            HashSet<Id<MediaItem>> wanted = [.. itemsByHash[hash]];
            groupByHash.TryGetValue(hash, out var group);
            if (wanted.Count < 2)
            {
                if (group is not null)
                {
                    db.DuplicateGroupMembers.RemoveRange(members[group.Id]);
                    db.DuplicateGroups.Remove(group);
                    deleted++;
                }

                continue;
            }

            if (group is null)
            {
                group = DuplicateGroup.CreateExact(hash, now);
                db.DuplicateGroups.Add(group);
                db.DuplicateGroupMembers.AddRange(wanted.Select(itemId => new DuplicateGroupMember(group.Id, itemId, now)));
                created++;
                continue;
            }

            var current = members[group.Id].ToList();
            var leaving = current.Where(member => !wanted.Contains(member.ItemId)).ToList();
            var joining = wanted.Except(current.Select(member => member.ItemId)).ToList();
            if (leaving.Count == 0 && joining.Count == 0)
            {
                continue;
            }

            db.DuplicateGroupMembers.RemoveRange(leaving);
            db.DuplicateGroupMembers.AddRange(joining.Select(itemId => new DuplicateGroupMember(group.Id, itemId, now)));

            // A new member makes a Resolved or Ignored group worth a look again (E12-S01).
            if (joining.Count > 0 && group.MemberJoined(now))
            {
                reopened++;
            }

            changed++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var processed = JsonSerializer.Serialize(texts);
        await db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "duplicate_dirty_hash" WHERE "content_hash" IN (SELECT "value" FROM json_each({processed}))""",
            cancellationToken).ConfigureAwait(false);
        return new DuplicateReconcileResult(texts.Count, created, changed, reopened, deleted);
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            LogListenerFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of the duplicate group changes failed")]
    private partial void LogListenerFailed(Exception exception);

    private sealed record OpenStatisticsRow(int OpenGroups, long WastedBytes);
}
