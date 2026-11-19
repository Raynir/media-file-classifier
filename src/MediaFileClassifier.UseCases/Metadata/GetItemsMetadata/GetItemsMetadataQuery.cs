using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.GetItemsMetadata;

/// <summary>
/// Gets the metadata of many items with every layer, e.g. for a multi-selection (E07-S01-T01).
/// </summary>
/// <param name="ItemIds">The items.</param>
/// <returns>The metadata of each distinct item, in the order given; empty if no library is open.</returns>
public sealed record GetItemsMetadataQuery(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<IReadOnlyList<ItemMetadataView>>;

/// <summary>
/// Handles <see cref="GetItemsMetadataQuery"/> for the open library, with one query per table.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemsMetadataQueryHandler(ILibrarySession session)
    : IRequestHandler<GetItemsMetadataQuery, IReadOnlyList<ItemMetadataView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMetadataView>> Handle(GetItemsMetadataQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0)
        {
            return [];
        }

        var ids = request.ItemIds.Distinct().ToList();
        var metadata = await library.Metadata.GetManyAsync(ids, cancellationToken).ConfigureAwait(false);
        return [.. ids.Select(id => ItemMetadataView.From(metadata[id]))];
    }
}
