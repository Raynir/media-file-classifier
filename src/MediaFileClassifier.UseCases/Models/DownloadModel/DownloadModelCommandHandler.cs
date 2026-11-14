using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.DownloadModel;

/// <summary>
/// Handles <see cref="DownloadModelCommand"/>.
/// </summary>
/// <param name="catalog">The model catalogue.</param>
/// <param name="downloader">The model downloader.</param>
public sealed class DownloadModelCommandHandler(IModelCatalog catalog, IModelDownloader downloader) : IRequestHandler<DownloadModelCommand, InstalledModel>
{
    /// <inheritdoc />
    public Task<InstalledModel> Handle(DownloadModelCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entry = catalog.GetDefault(request.Purpose)
            ?? throw new InvalidOperationException($"The model catalogue has no {request.Purpose} model.");
        var tier = request.TierId is null ? entry.Default : entry.GetTier(request.TierId);
        return downloader.DownloadAsync(entry, tier.TierId, request.Progress, cancellationToken);
    }
}
