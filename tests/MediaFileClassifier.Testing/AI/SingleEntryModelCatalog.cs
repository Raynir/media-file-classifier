using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// A model catalogue with one entry, which is also the default for its purpose.
/// </summary>
/// <param name="entry">The entry.</param>
public sealed class SingleEntryModelCatalog(ModelCatalogEntry entry) : IModelCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<ModelCatalogEntry> Entries => [entry];

    /// <inheritdoc />
    public ModelCatalogEntry? Find(string modelId) => modelId == entry.ModelId ? entry : null;

    /// <inheritdoc />
    public ModelCatalogEntry? GetDefault(ModelPurpose purpose) => purpose == entry.Purpose ? entry : null;
}
