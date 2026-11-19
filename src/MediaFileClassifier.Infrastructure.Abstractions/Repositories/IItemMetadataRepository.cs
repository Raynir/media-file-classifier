using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores each item's <see cref="ItemMetadata"/>: technical, AI and user values, tags, tombstones and analysis runs.
/// Changes go through the domain methods of <see cref="ItemMetadata"/>, which apply the effective-value rules;
/// <see cref="ItemMetadata.GetEffectiveValues"/> and <see cref="ItemMetadata.GetEffectiveTags"/> resolve the result.
/// </summary>
public interface IItemMetadataRepository
{
    /// <summary>
    /// Loads the metadata of an item. An item with no metadata yet gets an empty one.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata.</returns>
    Task<ItemMetadata> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the metadata of many items with one query per table, e.g. for a multi-selection.
    /// </summary>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata of each distinct item; an item with no metadata yet, or unknown, gets an empty one.</returns>
    Task<IReadOnlyDictionary<Id<MediaItem>, ItemMetadata>> GetManyAsync(
        IReadOnlyCollection<Id<MediaItem>> itemIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an item's metadata, applies a change and stores the result, as one write (D072).
    /// </summary>
    /// <typeparam name="TResult">What the change returns.</typeparam>
    /// <param name="itemId">The item.</param>
    /// <param name="change">The change, e.g. <c>m =&gt; m.ApplyRun(run, values, tags, 1)</c>. It runs on the writer,
    /// so it must not wait for other library writes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The changed metadata, and what the change returned.</returns>
    /// <exception cref="KeyNotFoundException">The item doesn't exist.</exception>
    Task<(ItemMetadata Metadata, TResult Result)> UpdateAsync<TResult>(
        Id<MediaItem> itemId,
        Func<ItemMetadata, TResult> change,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an item's metadata, applies a change and stores the result, as one write (D072).
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="change">The change, e.g. <c>m =&gt; m.EditValue(field, value, now)</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The changed metadata.</returns>
    /// <exception cref="KeyNotFoundException">The item doesn't exist.</exception>
    Task<ItemMetadata> UpdateAsync(Id<MediaItem> itemId, Action<ItemMetadata> change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user tag to many items, e.g. a multi-selection, and removes its tombstones (E07).
    /// The items are written in batches, each its own short write, so other writes such as the user's edits get their
    /// turn between batches (D072). If one fails or is cancelled, the batches before it stay written.
    /// </summary>
    /// <param name="itemIds">The items.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every item is tagged.</returns>
    Task AddTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a tag from many items and leaves tombstones, so neither the file nor an AI run adds it again (E07).
    /// The items are written in batches, each its own short write, so other writes such as the user's edits get their
    /// turn between batches (D072). If one fails or is cancelled, the batches before it stay written.
    /// </summary>
    /// <param name="itemIds">The items.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the tag is removed from every item.</returns>
    Task RemoveTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tags the user has given any item, with the number of items each, e.g. for tag autocompletion.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tags, most used first.</returns>
    Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetUserTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the effective tags (user, AI and technical, as <see cref="ItemMetadata.GetEffectiveTags"/> resolves them) that
    /// start with a prefix, ignoring case, with the number of items each, e.g. for the tag filter's autocompletion.
    /// </summary>
    /// <param name="prefix">The text the tags start with, or <c>null</c> or empty for every tag.</param>
    /// <param name="limit">The most tags to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tags, most used first, then by name.</returns>
    Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetEffectiveTagsAsync(string? prefix, int limit, CancellationToken cancellationToken = default);
}
