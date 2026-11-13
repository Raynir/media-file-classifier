using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.GetModels;

/// <summary>
/// Handles <see cref="GetModelsQuery"/>.
/// </summary>
/// <param name="catalog">The model catalogue.</param>
/// <param name="store">The model store.</param>
public sealed class GetModelsQueryHandler(IModelCatalog catalog, IModelStore store) : IRequestHandler<GetModelsQuery, IReadOnlyList<ModelListEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelListEntry>> Handle(GetModelsQuery request, CancellationToken cancellationToken)
    {
        var installed = (await store.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var entries = new List<ModelListEntry>();
        foreach (var model in catalog.Entries)
        {
            foreach (var tier in model.Tiers)
            {
                var match = installed.FirstOrDefault(item =>
                    item.ModelId == model.ModelId && item.Version == model.Version && item.TierId == tier.TierId);
                if (match is not null)
                {
                    installed.Remove(match);
                }

                entries.Add(new ModelListEntry(
                    model.ModelId,
                    model.Version,
                    tier.TierId,
                    model.DisplayName,
                    tier.DisplayName,
                    tier.Precision,
                    match?.Files.Sum(file => file.Size) ?? tier.Size,
                    model.License,
                    model.LicenseUrl,
                    model.Source,
                    model.Purpose,
                    tier.TierId == model.DefaultTier,
                    match?.InstalledAt));
            }
        }

        // Installed by hand, or an older version the catalogue no longer lists.
        entries.AddRange(installed.Select(item => new ModelListEntry(
            item.ModelId,
            item.Version,
            item.TierId,
            item.DisplayName,
            item.TierId,
            Precision: null,
            item.Files.Sum(file => file.Size),
            item.License,
            LicenseUrl: null,
            Uri.TryCreate(item.Source, UriKind.Absolute, out var source) ? source : null,
            Purpose: null,
            IsDefaultTier: false,
            item.InstalledAt)));
        return entries;
    }
}
