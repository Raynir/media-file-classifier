namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Pauses work for an AI provider that keeps failing, and resumes it once the provider is healthy again (E05-S04-T03).
/// </summary>
/// <remarks>
/// MFC-83 needs only <see cref="IsOpen"/>: the analysis stage checks it before calling a describer or OCR provider, and
/// skips that analyzer instead of calling a provider that's down, so embeddings keep running. MFC-103 adds the rest of
/// the interface and the implementation; until then no breaker is registered, and every circuit counts as closed.
/// </remarks>
public interface IProviderCircuitBreaker
{
    /// <summary>
    /// Gets whether a provider's circuit is open: work that uses it is paused.
    /// </summary>
    /// <param name="providerId">The provider's <see cref="IAiProvider.Id"/>.</param>
    /// <returns><c>true</c> while the provider is paused.</returns>
    bool IsOpen(string providerId);
}
