using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores <see cref="ItemMetadata"/>. An update loads the item's rows, applies the domain change and replaces the
/// rows, all in one write. An item has a few dozen metadata rows, so replacing them is cheap, and it keeps the domain
/// model free of change tracking.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
/// <param name="timeProvider">Clock, for the tag operations.</param>
internal sealed class ItemMetadataRepository(LibraryConnectionFactory connections, LibraryWriter writer, TimeProvider timeProvider)
    : IItemMetadataRepository
{
    /// <inheritdoc />
    public async Task<ItemMetadata> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default)
    {
        await using var db = await connections.CreateContextAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await LoadAsync(db, itemId, cancellationToken).ConfigureAwait(false);
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
    public async Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetUserTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await connections.CreateContextAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var tags = await db.TagAssignments
            .Where(tag => tag.Source == MetadataSource.User)
            .GroupBy(tag => tag.Tag)
            .Select(group => new { Tag = group.Key, ItemCount = group.Select(tag => tag.ItemId).Distinct().Count() })
            .OrderByDescending(tag => tag.ItemCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. tags.Select(tag => (tag.Tag, tag.ItemCount))];
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
        return new ItemMetadata(itemId, technical, user, runs, ai, tags, rejections);
    }

    private static async Task<ItemMetadata> LoadExistingAsync(LibraryDbContext db, Id<MediaItem> itemId, CancellationToken cancellationToken)
    {
        if (!await db.MediaItems.AnyAsync(item => item.Id == itemId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException($"Media item {itemId} doesn't exist.");
        }

        return await LoadAsync(db, itemId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceAsync(LibraryDbContext db, ItemMetadata metadata, CancellationToken cancellationToken)
    {
        var itemId = metadata.ItemId;

        // Dependents first: values and tags refer to runs.
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
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    private Task UpdateManyAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, Action<ItemMetadata> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                foreach (var itemId in itemIds.Distinct())
                {
                    var metadata = await LoadExistingAsync(db, itemId, token).ConfigureAwait(false);
                    change(metadata);
                    await ReplaceAsync(db, metadata, token).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}
