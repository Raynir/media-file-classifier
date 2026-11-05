namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// The models the app ships knowledge of (D032, E06-S12): what to download, from where, and the hashes to check.
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// Gets every catalogue entry.
    /// </summary>
    IReadOnlyList<ModelCatalogEntry> Entries { get; }

    /// <summary>
    /// Finds an entry by model ID.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <returns>The entry, or <see langword="null"/> if the catalogue has none.</returns>
    ModelCatalogEntry? Find(string modelId);

    /// <summary>
    /// Gets the model the app uses for a purpose: the first entry with it.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <returns>The entry, or <see langword="null"/> if the catalogue has none.</returns>
    ModelCatalogEntry? GetDefault(ModelPurpose purpose);
}
