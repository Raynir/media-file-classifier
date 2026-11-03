using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.Selection;

/// <summary>
/// Turns a selection into the IDs of the items it covers in the current results (E14-S03-T01), for bulk operations.
/// Items that no longer exist are left out.
/// </summary>
/// <param name="Selection">The selection.</param>
public sealed record ResolveItemSelectionQuery(ItemSelection Selection) : IRequest<IReadOnlyList<Id<MediaItem>>>;

/// <summary>
/// Handles <see cref="ResolveItemSelectionQuery"/> over the current results.
/// </summary>
/// <param name="source">The current results.</param>
public sealed class ResolveItemSelectionQueryHandler(IMediaResultSource source)
    : IRequestHandler<ResolveItemSelectionQuery, IReadOnlyList<Id<MediaItem>>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> Handle(ResolveItemSelectionQuery request, CancellationToken cancellationToken)
    {
        var selection = request.Selection;
        if (selection.Newest is not { } newest)
        {
            if (selection.Ids.Count == 0)
            {
                return [];
            }

            var existing = await source.GetExistingIdsAsync([.. selection.Ids], cancellationToken).ConfigureAwait(false);
            return [.. selection.Ids.Where(existing.Contains)];
        }

        var covered = await source.GetIdsAsync(0, int.MaxValue, newest, cancellationToken).ConfigureAwait(false);
        var added = selection.Ids.Where(id => !ItemSelection.IsUpTo(id, newest)).ToList();
        var addedExisting = added.Count == 0
            ? new HashSet<Id<MediaItem>>()
            : await source.GetExistingIdsAsync(added, cancellationToken).ConfigureAwait(false);
        return [.. covered.Where(id => !selection.Ids.Contains(id)), .. added.Where(addedExisting.Contains)];
    }
}
