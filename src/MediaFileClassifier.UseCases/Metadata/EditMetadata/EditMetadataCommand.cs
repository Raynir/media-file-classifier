using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// Applies a list of edits to one or more items (E07-S01-T02): the editor's Save, a review action or a bulk edit.
/// All edits of an item are applied in order in one transaction, with the item's keyword index row, so the item is
/// never half saved and the change is searchable on the next query. The edits are checked first (<see cref="MetadataValidator"/>,
/// <see cref="MetadataLimits"/>); if one is refused, nothing is written. Many items are written in batches. One
/// <see cref="ItemsMetadataChanged"/> is published with every changed item, also when a later batch fails.
/// </summary>
/// <param name="ItemIds">The items. Items that don't exist are skipped.</param>
/// <param name="Edits">The edits, applied in order.</param>
public sealed record EditMetadataCommand(IReadOnlyCollection<Id<MediaItem>> ItemIds, IReadOnlyList<MetadataEdit> Edits)
    : IRequest<EditMetadataResult>;

/// <summary>
/// The outcome of <see cref="EditMetadataCommand"/>.
/// </summary>
/// <param name="ChangedItemIds">The items the edits changed.</param>
/// <param name="Error">Why the edits were refused, or <see langword="null"/> if they were applied. Nothing is written
/// when there is an error.</param>
public sealed record EditMetadataResult(IReadOnlyList<Id<MediaItem>> ChangedItemIds, MetadataValidationError? Error = null);

/// <summary>
/// Handles <see cref="EditMetadataCommand"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="clock">The clock, for the change time.</param>
/// <param name="publisher">Publishes the change.</param>
public sealed class EditMetadataCommandHandler(ILibrarySession session, TimeProvider clock, IPublisher publisher)
    : IRequestHandler<EditMetadataCommand, EditMetadataResult>
{
    private const int TagCheckBatchSize = 100;

    /// <inheritdoc />
    public async Task<EditMetadataResult> Handle(EditMetadataCommand request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0 || request.Edits.Count == 0)
        {
            return new EditMetadataResult([]);
        }

        var (edits, error) = MetadataValidator.Validate(request.Edits);
        if (error is null)
        {
            error = await CheckTagLimitAsync(library, request.ItemIds, edits, cancellationToken).ConfigureAwait(false);
        }

        if (error is not null)
        {
            return new EditMetadataResult([], error);
        }

        // Batches already written stay written if a later one fails, so the views hear about them either way. What
        // changed is kept per item, so the notification names only fields and tags that really changed.
        var written = new List<Id<MediaItem>>();
        var changes = new Dictionary<Id<MediaItem>, (HashSet<DescriptiveField> Fields, bool Tags)>();
        var completed = false;
        try
        {
            await library.Metadata.UpdateManyAsync(
                request.ItemIds,
                metadata =>
                {
                    var now = clock.GetUtcNow();
                    var fields = new HashSet<DescriptiveField>();
                    var tags = false;
                    foreach (var edit in edits)
                    {
                        if (edit.ApplyTo(metadata, now))
                        {
                            if (edit.ChangedField is { } field)
                            {
                                fields.Add(field);
                            }
                            else
                            {
                                tags = true;
                            }
                        }
                    }

                    changes[metadata.ItemId] = (fields, tags);
                    return fields.Count > 0 || tags;
                },
                written.AddRange,
                cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (written.Count > 0)
            {
                try
                {
                    await publisher.Publish(Describe(written, changes), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception) when (!completed)
                {
                    // The write's own failure is the one to report, not a handler's that ran because of it.
                }
            }
        }

        return new EditMetadataResult(written);
    }

    // The 200-tag limit depends on what an item has, so the edits are tried on the loaded metadata first, without
    // storing it: a bulk edit that would push one item over the limit writes nothing, not even the batches before it.
    private async Task<MetadataValidationError?> CheckTagLimitAsync(
        IOpenLibrary library,
        IReadOnlyCollection<Id<MediaItem>> itemIds,
        IReadOnlyList<MetadataEdit> edits,
        CancellationToken cancellationToken)
    {
        if (!edits.Any(edit => edit is MetadataEdit.AddTag or MetadataEdit.ConfirmTag or MetadataEdit.AcceptTagSuggestion))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        foreach (var batch in itemIds.Distinct().Chunk(TagCheckBatchSize))
        {
            var loaded = await library.Metadata.GetManyAsync(batch, cancellationToken).ConfigureAwait(false);
            foreach (var (itemId, metadata) in loaded)
            {
                var before = CountUserTags(metadata);
                foreach (var edit in edits)
                {
                    edit.ApplyTo(metadata, now);
                }

                var after = CountUserTags(metadata);
                if (after > MetadataLimits.UserTagsPerItem && after > before)
                {
                    return new MetadataValidationError(MetadataValidationCode.TooManyTags, Limit: MetadataLimits.UserTagsPerItem, ItemId: itemId);
                }
            }
        }

        return null;
    }

    private static int CountUserTags(ItemMetadata metadata) =>
        metadata.Tags.Where(tag => tag.Source == MetadataSource.User).Select(tag => tag.Tag).Distinct().Count();

    private static ItemsMetadataChanged Describe(
        IReadOnlyList<Id<MediaItem>> itemIds,
        Dictionary<Id<MediaItem>, (HashSet<DescriptiveField> Fields, bool Tags)> changes)
    {
        var fields = itemIds.SelectMany(id => changes[id].Fields).ToHashSet();
        var change = (fields.Count > 0 ? ItemMetadataChange.Fields : 0) | (itemIds.Any(id => changes[id].Tags) ? ItemMetadataChange.Tags : 0);
        return new ItemsMetadataChanged(itemIds, change) { Fields = fields };
    }
}
