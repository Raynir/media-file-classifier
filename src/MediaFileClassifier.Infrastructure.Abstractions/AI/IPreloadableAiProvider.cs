namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// A provider whose model takes long to load, so callers can load it ahead of the first request and release its
/// memory when it's no longer needed, e.g. the text tower while a library is open (E06-S03-T02).
/// </summary>
public interface IPreloadableAiProvider : IAiProvider
{
    /// <summary>
    /// Loads the model, or the part of it that serves one capability.
    /// </summary>
    /// <param name="capability">The capability to load for, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    /// <exception cref="AiProviderException">The model isn't installed or doesn't load.</exception>
    Task PreloadAsync(AiCapability? capability, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the loaded model. The next request loads it again.
    /// </summary>
    void Unload();
}
