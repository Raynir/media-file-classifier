using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediatR;

namespace MediaFileClassifier.UseCases.Models.ImportModel;

/// <summary>
/// Handles <see cref="ImportModelCommand"/>.
/// </summary>
/// <param name="catalog">The model catalogue.</param>
/// <param name="store">The model store.</param>
public sealed class ImportModelCommandHandler(IModelCatalog catalog, IModelStore store) : IRequestHandler<ImportModelCommand, InstalledModel>
{
    /// <inheritdoc />
    /// <exception cref="ModelStoreException">The files don't make up a tier of the model, or don't match its hashes.</exception>
    public Task<InstalledModel> Handle(ImportModelCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entry = catalog.GetDefault(request.Purpose)
            ?? throw new InvalidOperationException($"The model catalogue has no {request.Purpose} model.");

        var byName = request.Paths
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // The default tier first, then the others: the first tier whose files were all picked.
        var tier = entry.Tiers
            .OrderBy(tier => tier.TierId == entry.DefaultTier ? 0 : 1)
            .FirstOrDefault(tier => tier.Files.All(file => byName.ContainsKey(file.FileName)))
            ?? throw new ModelStoreException(
                $"Pick all the files of {entry.DisplayName}: {string.Join(", ", entry.Default.Files.Select(file => file.FileName))}.");

        var sources = tier.Files.Select(file => new ModelFileSource(file.Role, byName[file.FileName], file.Sha256, file.FileName)).ToList();
        var import = new ModelImportRequest(entry.ModelId, entry.Version, tier.TierId, entry.DisplayName, sources)
        {
            License = entry.License,
            Source = entry.Source.ToString(),
        };
        return store.ImportAsync(import, request.Progress, cancellationToken);
    }
}
