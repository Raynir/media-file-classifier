using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Infrastructure.AI.Models;

/// <summary>
/// Checks the model store for the catalogue's model for each purpose (D037). A model counts as installed when the
/// catalogue entry's ID and version are in the store, in any tier, and whether it was downloaded or imported.
/// </summary>
public sealed class ModelAvailability : IModelAvailability, IDisposable
{
    private readonly IModelCatalog catalog;
    private readonly IModelStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelAvailability"/> class.
    /// </summary>
    /// <param name="catalog">The model catalogue.</param>
    /// <param name="store">The model store.</param>
    public ModelAvailability(IModelCatalog catalog, IModelStore store)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(store);

        this.catalog = catalog;
        this.store = store;
        store.ModelsChanged += OnModelsChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<bool> IsInstalledAsync(ModelPurpose purpose, CancellationToken cancellationToken) =>
        catalog.GetDefault(purpose) is { } entry
        && (await store.FindTiersAsync(entry.ModelId, entry.Version, cancellationToken).ConfigureAwait(false)).Count > 0;

    /// <inheritdoc />
    public void Dispose() => store.ModelsChanged -= OnModelsChanged;

    private void OnModelsChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
