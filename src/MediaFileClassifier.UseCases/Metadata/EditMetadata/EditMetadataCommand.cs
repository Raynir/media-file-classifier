using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// Applies a list of edits to one or more items (E07-S01-T02): the editor's Save, a review action or a bulk edit.
/// All edits of an item are applied in order in one transaction, with the item's keyword index row, so the item is
/// never half saved and the change is searchable on the next query. Many items are written in batches.
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
public sealed class EditMetadataCommandHandler(ILibrarySession session, TimeProvider clock)
    : IRequestHandler<EditMetadataCommand, EditMetadataResult>
{
    /// <inheritdoc />
    public async Task<EditMetadataResult> Handle(EditMetadataCommand request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0 || request.Edits.Count == 0)
        {
            return new EditMetadataResult([]);
        }

        var changed = await library.Metadata.UpdateManyAsync(
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
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new EditMetadataResult(changed);
    }
}
