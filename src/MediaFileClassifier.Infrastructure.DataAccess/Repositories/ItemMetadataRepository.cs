using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using MediaFileClassifier.Infrastructure.DataAccess.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores <see cref="ItemMetadata"/>. An update loads the item's rows, applies the domain change and replaces the
/// rows, all in one write. An item has a few dozen metadata rows, so replacing them is cheap, and it keeps the domain
/// model free of change tracking. The same write updates the item's keyword index row (E01-S04-T02), so a change is
/// searchable as soon as it commits.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="timeProvider">Clock, for the tag operations.</param>
internal sealed class ItemMetadataRepository(LibraryConnectionFactory connections, LibraryWriter writer, TimeProvider timeProvider)
    : IItemMetadataRepository
{
    /// <inheritdoc />
    public Task<ItemMetadata> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync((db, token) => LoadAsync(db, itemId, token), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Id<MediaItem>, ItemMetadata>> GetManyAsync(
        IReadOnlyCollection<Id<MediaItem>> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        return connections.ReadAsync((db, token) => LoadManyAsync(db, itemIds, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<(ItemMetadata Metadata, TResult Result)> UpdateAsync<TResult>(
        Id<MediaItem> itemId,
        Func<ItemMetadata, TResult> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var metadata = await LoadExistingAsync(db, itemId, token).ConfigureAwait(false);
                var result = change(metadata);
                await ReplaceAsync(db, metadata, token).ConfigureAwait(false);
                return (metadata, result);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItemMetadata> UpdateAsync(Id<MediaItem> itemId, Action<ItemMetadata> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var (metadata, _) = await UpdateAsync(
            itemId,
            value =>
            {
                change(value);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        return metadata;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> UpdateManyAsync(
        IReadOnlyCollection<Id<MediaItem>> itemIds,
        Func<ItemMetadata, bool> change,
        Action<IReadOnlyList<Id<MediaItem>>>? written = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(change);

        var changed = new List<Id<MediaItem>>();
        foreach (var batch in itemIds.Distinct().Chunk(MediaItemRepository.BatchSize))
        {
            var batchChanged = await writer.ExecuteAsync(
                async (db, token) =>
                {
                    var ids = batch.ToList();
                    var existing = (await db.MediaItems.AsNoTracking().Where(item => ids.Contains(item.Id)).Select(item => item.Id)
                        .ToListAsync(token).ConfigureAwait(false)).ToHashSet();
                    var result = new List<Id<MediaItem>>();
                    var loaded = await LoadManyAsync(db, [.. batch.Where(existing.Contains)], token).ConfigureAwait(false);
                    foreach (var itemId in batch.Where(existing.Contains))
                    {
                        var metadata = loaded[itemId];
                        if (change(metadata))
                        {
                            await ReplaceAsync(db, metadata, token).ConfigureAwait(false);
                            result.Add(itemId);
                        }
                    }

                    return result;
                },
                cancellationToken).ConfigureAwait(false);
            changed.AddRange(batchChanged);
            if (batchChanged.Count > 0)
            {
                written?.Invoke(batchChanged);
            }
        }

        return changed;
    }

    /// <inheritdoc />
    public Task AddTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return UpdateManyAsync(itemIds, metadata => metadata.AddTag(tag, timeProvider.GetUtcNow()), cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return UpdateManyAsync(itemIds, metadata => metadata.RejectTag(tag, timeProvider.GetUtcNow()), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetUserTagsAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<(TagName Tag, int ItemCount)>>(
            async (db, token) =>
            {
                // Grouped by tag_key, so tags that differ only in case are one tag in every alphabet. Spellings of one
                // tag can differ between items; min() picks one of them, the same one every time.
                var tags = await db.Database
                    .SqlQuery<UserTagRow>(
                        $"""
                        SELECT min(tag) AS Tag, count(DISTINCT item_id) AS ItemCount
                        FROM tag_assignment
                        WHERE source = {(int)MetadataSource.User}
                        GROUP BY tag_key
                        ORDER BY ItemCount DESC, tag_key
                        """)
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return [.. tags.Select(tag => (TagName.From(tag.Tag), tag.ItemCount))];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetEffectiveTagsAsync(string? prefix, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // tag_key is the tag in upper case (D071), so an upper-case prefix ignores case in every alphabet. A range on it,
        // unlike LIKE, uses its index: every key that starts with the prefix sorts below the prefix and U+10FFFF.
        var key = string.IsNullOrWhiteSpace(prefix) ? string.Empty : TagKeyGenerator.KeyOf(TagName.From(prefix));

        // Only numbers and SQL the code writes itself go into the text; the prefix and the limit are parameters.
        // Excluded items never reach the results, so their tags aren't offered.
        // A tag is offered by its own text and by its other-language form (other_form_key), each through its index; the
        // filter matches either, so the counts agree with the results the offered tag gives.
        var sql = $"""
            SELECT min(t."label") AS Tag, count(DISTINCT t."item_id") AS ItemCount
            FROM (
                SELECT a."tag_key" AS "key", a."tag" AS "label", a."item_id"
                FROM "tag_assignment" a
                JOIN "media_item" m ON m."id" = a."item_id"
                WHERE a."tag_key" >= $from AND a."tag_key" < $to AND m."status" <> {(int)MediaItemStatus.Excluded} AND {EffectiveTagSql.Condition("a")}
                UNION ALL
                SELECT a."other_form_key", a."other_form", a."item_id"
                FROM "tag_assignment" a
                JOIN "media_item" m ON m."id" = a."item_id"
                WHERE a."other_form_key" >= $from AND a."other_form_key" < $to AND m."status" <> {(int)MediaItemStatus.Excluded} AND {EffectiveTagSql.Condition("a")}
            ) t
            GROUP BY t."key"
            ORDER BY ItemCount DESC, t."key"
            LIMIT $limit
            """;
        return connections.ReadAsync<IReadOnlyList<(TagName Tag, int ItemCount)>>(
            async (db, token) =>
            {
                var tags = await db.Database
                    .SqlQueryRaw<UserTagRow>(sql, new SqliteParameter("$from", key), new SqliteParameter("$to", key + "􏿿"), new SqliteParameter("$limit", limit))
                    .ToListAsync(token)
                    .ConfigureAwait(false);
                return [.. tags.Select(tag => (TagName.From(tag.Tag), tag.ItemCount))];
            },
            cancellationToken);
    }

    private static async Task<ItemMetadata> LoadAsync(LibraryDbContext db, Id<MediaItem> itemId, CancellationToken cancellationToken)
    {
        var technical = await db.TechnicalFieldValues.AsNoTracking().Where(value => value.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var user = await db.UserFieldValues.AsNoTracking().Where(value => value.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var runs = await db.AnalysisRuns.AsNoTracking().Where(run => run.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ai = await db.AiFieldValues.AsNoTracking().Where(value => value.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var tags = await db.TagAssignments.AsNoTracking().Where(tag => tag.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rejections = await db.TagRejections.AsNoTracking().Where(rejection => rejection.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var suggestions = await db.TagSuggestions.AsNoTracking().Where(suggestion => suggestion.ItemId == itemId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ItemMetadata(itemId, technical, user, runs, ai, tags, rejections, suggestions);
    }

    /// <summary>
    /// Loads the metadata of many items with one query per table, e.g. for a search index rebuild.
    /// </summary>
    /// <param name="db">A context.</param>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata of each item; an item with none gets an empty one.</returns>
    internal static async Task<IReadOnlyDictionary<Id<MediaItem>, ItemMetadata>> LoadManyAsync(
        LibraryDbContext db,
        IReadOnlyCollection<Id<MediaItem>> itemIds,
        CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        var technical = (await db.TechnicalFieldValues.AsNoTracking().Where(value => ids.Contains(value.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(value => value.ItemId);
        var user = (await db.UserFieldValues.AsNoTracking().Where(value => ids.Contains(value.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(value => value.ItemId);
        var runs = (await db.AnalysisRuns.AsNoTracking().Where(run => ids.Contains(run.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(run => run.ItemId);
        var ai = (await db.AiFieldValues.AsNoTracking().Where(value => ids.Contains(value.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(value => value.ItemId);
        var tags = (await db.TagAssignments.AsNoTracking().Where(tag => ids.Contains(tag.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(tag => tag.ItemId);
        var rejections = (await db.TagRejections.AsNoTracking().Where(rejection => ids.Contains(rejection.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(rejection => rejection.ItemId);
        var suggestions = (await db.TagSuggestions.AsNoTracking().Where(suggestion => ids.Contains(suggestion.ItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToLookup(suggestion => suggestion.ItemId);
        return ids.ToDictionary(
            id => id,
            id => new ItemMetadata(id, technical[id], user[id], runs[id], ai[id], tags[id], rejections[id], suggestions[id]));
    }

    internal static async Task<ItemMetadata> LoadExistingAsync(LibraryDbContext db, Id<MediaItem> itemId, CancellationToken cancellationToken)
    {
        if (!await db.MediaItems.AnyAsync(item => item.Id == itemId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException($"Media item {itemId} doesn't exist.");
        }

        return await LoadAsync(db, itemId, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ReplaceAsync(LibraryDbContext db, ItemMetadata metadata, CancellationToken cancellationToken)
    {
        var itemId = metadata.ItemId;

        // Dependents first: values and tags refer to runs.
        await db.TagSuggestions.Where(suggestion => suggestion.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.TagAssignments.Where(tag => tag.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.AiFieldValues.Where(value => value.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.UserFieldValues.Where(value => value.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.TagRejections.Where(rejection => rejection.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.TechnicalFieldValues.Where(value => value.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.AnalysisRuns.Where(run => run.ItemId == itemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        db.AnalysisRuns.AddRange(metadata.Runs);
        db.TechnicalFieldValues.AddRange(metadata.TechnicalValues);
        db.UserFieldValues.AddRange(metadata.UserValues);
        db.AiFieldValues.AddRange(metadata.AiValues);
        db.TagAssignments.AddRange(metadata.Tags);
        db.TagRejections.AddRange(metadata.Rejections);
        db.TagSuggestions.AddRange(metadata.TagSuggestions);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        await SearchStore.UpdateTextAsync(db, metadata, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateManyAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, Action<ItemMetadata> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        // In batches, like MediaItemRepository.AddRangeAsync, so tagging a large selection doesn't hold up other writes.
        foreach (var batch in itemIds.Distinct().Chunk(MediaItemRepository.BatchSize))
        {
            await writer.ExecuteAsync(
                async (db, token) =>
                {
                    foreach (var itemId in batch)
                    {
                        var metadata = await LoadExistingAsync(db, itemId, token).ConfigureAwait(false);
                        change(metadata);
                        await ReplaceAsync(db, metadata, token).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A row of <see cref="GetUserTagsAsync"/> and <see cref="GetEffectiveTagsAsync"/>.
    /// </summary>
    /// <param name="Tag">One spelling of the tag.</param>
    /// <param name="ItemCount">Number of items with the tag.</param>
    private sealed record UserTagRow(string Tag, int ItemCount);
}
