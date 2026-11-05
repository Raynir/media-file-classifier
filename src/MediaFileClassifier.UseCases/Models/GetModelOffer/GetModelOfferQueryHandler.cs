using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModelOffer;

/// <summary>
/// Handles <see cref="GetModelOfferQuery"/>.
/// </summary>
/// <param name="catalog">The model catalogue.</param>
/// <param name="availability">Which models are installed.</param>
public sealed class GetModelOfferQueryHandler(IModelCatalog catalog, IModelAvailability availability) : IRequestHandler<GetModelOfferQuery, ModelOffer?>
{
    /// <inheritdoc />
    public async Task<ModelOffer?> Handle(GetModelOfferQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (catalog.GetDefault(request.Purpose) is not { } entry)
        {
            return null;
        }

        var installed = await availability.IsInstalledAsync(request.Purpose, cancellationToken).ConfigureAwait(false);
        return new ModelOffer(entry.DisplayName, entry.Default.Size, entry.License, entry.LicenseUrl, entry.Source, installed);
    }
}
