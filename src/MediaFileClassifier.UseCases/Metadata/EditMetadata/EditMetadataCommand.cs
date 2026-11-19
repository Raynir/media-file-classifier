using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// Applies a list of edits to one or more items (E07-S01-T02): the editor's Save, a review action or a bulk edit.
/// All edits of an item are applied in order in one transaction, with the item's keyword index row, so the item is
/// never half saved and the change is searchable on the next query. Many items are written in batches. One
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
public sealed record EditMetadataResult(IReadOnlyList<Id<MediaItem>> ChangedItemIds);

/// <summary>
/// Handles <see cref="EditMetadataCommand"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="clock">The clock, for the change time.</param>
/// <param name="publisher">Publishes the change.</param>
public sealed class EditMetadataCommandHandler(ILibrarySession session, TimeProvider clock, IPublisher publisher)
    : IRequestHandler<EditMetadataCommand, EditMetadataResult>
{
    /// <inheritdoc />
    public async Task<EditMetadataResult> Handle(EditMetadataCommand request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0 || request.Edits.Count == 0)
        {
            return new EditMetadataResult([]);
        }

        // Batches already written stay written if a later one fails, so the views hear about them either way.
        var written = new List<Id<MediaItem>>();
        try
        {
            await library.Metadata.UpdateManyAsync(
                request.ItemIds,
                metadata =>
                {
                    var now = clock.GetUtcNow();
                    var any = false;
                    foreach (var edit in request.Edits)
                    {
                        any |= edit.ApplyTo(metadata, now);
                    }

                    return any;
                },
                written.AddRange,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (written.Count > 0)
            {
                await publisher.Publish(Describe(written, request.Edits), CancellationToken.None).ConfigureAwait(false);
            }
        }

        return new EditMetadataResult(written);
    }

    private static ItemsMetadataChanged Describe(IReadOnlyList<Id<MediaItem>> itemIds, IReadOnlyList<MetadataEdit> edits)
    {
        var fields = edits.Select(edit => edit.ChangedField).OfType<DescriptiveField>().ToHashSet();
        var change = (fields.Count > 0 ? ItemMetadataChange.Fields : 0) | (edits.Any(edit => edit.ChangedField is null) ? ItemMetadataChange.Tags : 0);
        return new ItemsMetadataChanged(itemIds, change) { Fields = fields };
    }
}
