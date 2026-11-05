namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// Tells whether the app's model for a purpose is installed (D037): jobs that need a missing model wait for it, and
/// the "no model" banner shows while the embedding model is missing (E06-S12).
/// </summary>
public interface IModelAvailability
{
    /// <summary>
    /// Raised after a model is installed or deleted, on the thread that changed the store.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Checks whether the catalogue's model for a purpose is installed.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if it's installed.</returns>
    Task<bool> IsInstalledAsync(ModelPurpose purpose, CancellationToken cancellationToken);
}
