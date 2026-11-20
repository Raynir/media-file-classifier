using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Metadata.GetItemMetadata;

/// <summary>
/// Gets an item's metadata with every layer (E07-S01-T01).
/// </summary>
/// <param name="ItemId">The item.</param>
/// <returns>The metadata, or <see langword="null"/> if no library is open.</returns>
public sealed record GetItemMetadataQuery(Id<MediaItem> ItemId) : IRequest<ItemMetadataView?>;

/// <summary>
/// Handles <see cref="GetItemMetadataQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemMetadataQueryHandler(ILibrarySession session) : IRequestHandler<GetItemMetadataQuery, ItemMetadataView?>
{
    /// <inheritdoc />
    public async Task<ItemMetadataView?> Handle(GetItemMetadataQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return null;
        }

        var metadata = await library.Metadata.GetAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
        var models = await library.Models.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return ItemMetadataView.From(metadata) with
        {
            ModelNames = models.ToDictionary(model => model.Id, model => model.ModelName),
        };
    }
}
