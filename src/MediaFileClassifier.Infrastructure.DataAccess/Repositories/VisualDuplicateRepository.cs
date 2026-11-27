using System.Text.Json;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores the visual near-duplicate queue and groups (E12-S02). The <c>VisualDuplicateQueue</c> migration's triggers
/// fill <c>duplicate_dirty_item</c>; <see cref="IVisualDuplicateRepository.ApplyAsync"/> writes a matching result on the writer (D072).
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="groups">The duplicate groups, whose <see cref="IDuplicateGroupRepository.Changed"/> this raises.</param>
internal sealed class VisualDuplicateRepository(LibraryConnectionFactory connections, LibraryWriter writer, DuplicateGroupRepository groups)
    : IVisualDuplicateRepository
{
    /// <inheritdoc />
    public Task<VisualQueueBatch> PeekQueueAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return connections.ReadAsync(
            async (db, token) =>
            {
                var rows = await db.Database
                    .SqlQuery<QueueRow>($"""SELECT "seq" AS "Sequence", "item_id" AS "ItemId" FROM "duplicate_dirty_item" ORDER BY "seq" LIMIT {limit}""")
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                // A row that isn't an item ID is taken off with the others, so it never blocks the queue.
                List<Id<MediaItem>> items = [.. rows
                    .Select(row => Guid.TryParse(row.ItemId, out var id) ? new Id<MediaItem>(id) : (Id<MediaItem>?)null)
                    .OfType<Id<MediaItem>>()];
                return rows.Count == 0 ? VisualQueueBatch.Empty : new VisualQueueBatch(items, [.. rows.Select(row => row.Sequence)]);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountQueuedAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            (db, token) => db.Database.SqlQueryRaw<int>("""SELECT count(*) AS "Value" FROM "duplicate_dirty_item" """).SingleAsync(token),
            cancellationToken);

    /// <inheritdoc />
    public Task<int> QueueAllAsync(CancellationToken cancellationToken = default) =>
        writer.ExecuteAsync(
            async (db, token) =>
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT OR IGNORE INTO "duplicate_dirty_item" ("item_id")
                    SELECT "id" FROM "media_item" WHERE "hashes_perceptual_hash" IS NOT NULL
                    UNION SELECT DISTINCT "item_id" FROM "keyframe" WHERE "perceptual_hash" IS NOT NULL
                    UNION SELECT "m"."item_id" FROM "duplicate_group_member" AS "m"
                        JOIN "duplicate_group" AS "g" ON "g"."id" = "m"."group_id" WHERE "g"."type" = 1
                    """,
                    token).ConfigureAwait(false);
                return await db.Database.SqlQueryRaw<int>("""SELECT count(*) AS "Value" FROM "duplicate_dirty_item" """).SingleAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<VisualCandidate>> GetCandidatesAsync(IReadOnlyCollection<Id<MediaItem>>? itemIds, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<VisualCandidate>>(
            async (db, token) =>
            {
                List<Id<MediaItem>>? ids = itemIds is null ? null : [.. itemIds];
                var items = db.MediaItems.Where(item => item.Status != MediaItemStatus.Missing && item.Status != MediaItemStatus.Excluded);
                var keyframes = db.Keyframes.Where(keyframe => keyframe.PerceptualHash != null);
                if (ids is not null)
                {
                    items = items.Where(item => ids.Contains(item.Id));
                    keyframes = keyframes.Where(keyframe => ids.Contains(keyframe.ItemId));
                }

                var rows = await items
                    .Select(item => new { item.Id, item.MediaType, item.Hashes.ContentHash, item.Hashes.PerceptualHash })
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                var frames = (await keyframes
                        .Select(keyframe => new { keyframe.ItemId, keyframe.Index, keyframe.PerceptualHash })
                        .ToListAsync(token)
                        .ConfigureAwait(false))
                    .ToLookup(keyframe => keyframe.ItemId);
                var durations = await (ids is null ? db.TechnicalMetadata : db.TechnicalMetadata.Where(metadata => ids.Contains(metadata.ItemId)))
                    .Where(metadata => metadata.Duration != null)
                    .Select(metadata => new { metadata.ItemId, metadata.Duration })
                    .ToDictionaryAsync(metadata => metadata.ItemId, metadata => metadata.Duration, token)
                    .ConfigureAwait(false);

                return [.. rows
                    .Select(row => new VisualCandidate(
                        row.Id,
                        row.MediaType,
                        row.ContentHash,
                        row.PerceptualHash,
                        [.. frames[row.Id].OrderBy(frame => frame.Index).Select(frame => frame.PerceptualHash!.Value)],
                        durations.GetValueOrDefault(row.Id)))
                    .Where(candidate => candidate.PerceptualHash is not null || candidate.FrameHashes.Count > 0)
                    .OrderBy(candidate => candidate.ItemId.Value)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyList<Id<MediaItem>>>> GetGroupsOfAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        return connections.ReadAsync<IReadOnlyList<IReadOnlyList<Id<MediaItem>>>>(
            async (db, token) =>
            {
                var members = await LoadGroupsAsync(db, [.. itemIds], token).ConfigureAwait(false);
                return [.. members.Select(group => (IReadOnlyList<Id<MediaItem>>)[.. group.Select(member => member.ItemId).OrderBy(itemId => itemId.Value)])];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VisualReconcileResult> ApplyAsync(VisualGroupUpdate update, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var result = await writer.ExecuteAsync((db, token) => ApplyAsync(db, update, now, token), cancellationToken).ConfigureAwait(false);
        if (result.ChangedGroups)
        {
            groups.RaiseChanged();
        }

        return result;
    }

    private static async Task<List<IGrouping<Id<DuplicateGroup>, DuplicateGroupMember>>> LoadGroupsAsync(LibraryDbContext db, List<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        var groupIds = await db.DuplicateGroupMembers
            .Where(member => itemIds.Contains(member.ItemId))
            .Join(db.DuplicateGroups.Where(group => group.Type == DuplicateGroupType.Visual), member => member.GroupId, group => group.Id, (member, group) => group.Id)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var members = await db.DuplicateGroupMembers
            .Where(member => groupIds.Contains(member.GroupId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. members.GroupBy(member => member.GroupId)];
    }

    private static async Task<VisualReconcileResult> ApplyAsync(LibraryDbContext db, VisualGroupUpdate update, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Matching ran on a snapshot: leave out members deleted, Missing or Excluded since. Their change queued them
        // again, so the next check fixes the group.
        List<Id<MediaItem>> wanted = [.. update.Groups.SelectMany(group => group).Distinct()];
        var live = (await db.MediaItems
                .Where(item => wanted.Contains(item.Id) && item.Status != MediaItemStatus.Missing && item.Status != MediaItemStatus.Excluded)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();
        var found = update.Groups
            .Select(group => group.Where(live.Contains).ToHashSet())
            .Where(group => group.Count >= 2)
            .OrderBy(group => group.Min(itemId => itemId.Value))
            .ToList();

        List<Id<MediaItem>> scope = [.. update.Scope.Concat(wanted).Distinct()];
        var existing = await LoadGroupsAsync(db, scope, cancellationToken).ConfigureAwait(false);
        List<Id<DuplicateGroup>> existingIds = [.. existing.Select(group => group.Key)];
        var entities = await db.DuplicateGroups
            .Where(group => existingIds.Contains(group.Id))
            .ToDictionaryAsync(group => group.Id, cancellationToken)
            .ConfigureAwait(false);

        int created = 0, changed = 0, reopened = 0, deleted = 0;

        // Each old group goes to the new group it overlaps most, largest overlaps first; ties go to the older group
        // and then to the new group with the lowest item. The new group keeps the old one's ID and state.
        var pairs = found
            .SelectMany((group, index) => existing.Select(old => (Index: index, Old: old, Overlap: old.Count(member => group.Contains(member.ItemId)))))
            .Where(pair => pair.Overlap > 0)
            .OrderByDescending(pair => pair.Overlap)
            .ThenBy(pair => entities[pair.Old.Key].CreatedAt)
            .ThenBy(pair => pair.Old.Key.Value)
            .ThenBy(pair => pair.Index);
        var claimed = new HashSet<Id<DuplicateGroup>>();
        var matches = new Dictionary<int, IGrouping<Id<DuplicateGroup>, DuplicateGroupMember>>();
        foreach (var pair in pairs)
        {
            if (!matches.ContainsKey(pair.Index) && claimed.Add(pair.Old.Key))
            {
                matches[pair.Index] = pair.Old;
            }
        }

        for (var index = 0; index < found.Count; index++)
        {
            var group = found[index];
            if (!matches.TryGetValue(index, out var match))
            {
                var entity = DuplicateGroup.CreateVisual(now);
                db.DuplicateGroups.Add(entity);
                db.DuplicateGroupMembers.AddRange(group.OrderBy(itemId => itemId.Value).Select(itemId => new DuplicateGroupMember(entity.Id, itemId, now)));
                created++;
                continue;
            }

            var leaving = match.Where(member => !group.Contains(member.ItemId)).ToList();
            var joining = group.Except(match.Select(member => member.ItemId)).OrderBy(itemId => itemId.Value).ToList();
            if (leaving.Count == 0 && joining.Count == 0)
            {
                continue;
            }

            db.DuplicateGroupMembers.RemoveRange(leaving);
            db.DuplicateGroupMembers.AddRange(joining.Select(itemId => new DuplicateGroupMember(match.Key, itemId, now)));

            // A new member makes a Resolved or Ignored group worth a look again (D578).
            if (joining.Count > 0 && entities[match.Key].MemberJoined(now))
            {
                reopened++;
            }

            changed++;
        }

        foreach (var old in existing.Where(old => !claimed.Contains(old.Key)))
        {
            db.DuplicateGroupMembers.RemoveRange(old);
            db.DuplicateGroups.Remove(entities[old.Key]);
            deleted++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // An item queued again after the check has a new entry, which stays for the next check.
        var taken = 0;
        if (!update.Checked.IsEmpty)
        {
            var sequences = JsonSerializer.Serialize(update.Checked.Sequences);
            taken = await db.Database.ExecuteSqlAsync(
                $"""DELETE FROM "duplicate_dirty_item" WHERE "seq" IN (SELECT "value" FROM json_each({sequences}))""",
                cancellationToken).ConfigureAwait(false);
        }

        return new VisualReconcileResult(taken, created, changed, reopened, deleted);
    }

    private sealed record QueueRow(long Sequence, string ItemId);
}
