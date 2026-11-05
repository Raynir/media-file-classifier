namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Knows every configured AI provider and finds them by capability, so the pipeline never names a concrete provider
/// (E01-S05-T03). Providers come from dependency injection (built-in ones) and from <see cref="Register"/> (ones the
/// user configures, such as an LM Studio endpoint).
/// </summary>
public interface IAiProviderRegistry
{
    /// <summary>
    /// Gets every registered provider, including ones that don't run on this platform.
    /// </summary>
    IReadOnlyList<IAiProvider> Providers { get; }

    /// <summary>
    /// Adds a provider.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <exception cref="ArgumentException">A provider with the same ID is already registered.</exception>
    void Register(IAiProvider provider);

    /// <summary>
    /// Removes a provider. The caller still owns it and disposes it.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    /// <returns><c>true</c> if it was registered.</returns>
    bool Unregister(string providerId);

    /// <summary>
    /// Finds a provider by ID.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    /// <returns>The provider, or <c>null</c>.</returns>
    IAiProvider? Find(string providerId);

    /// <summary>
    /// Gets the providers that implement a capability and run on this platform, in registration order.
    /// </summary>
    /// <typeparam name="TCapability">Capability interface, e.g. <see cref="IImageDescriber"/>.</typeparam>
    /// <returns>The providers. Health isn't checked; see <see cref="GetLastHealth"/>.</returns>
    IReadOnlyList<TCapability> GetProviders<TCapability>()
        where TCapability : class, IAiProvider;

    /// <summary>
    /// Finds the text embedder that shares the vector space of an image embedding model, for search queries.
    /// </summary>
    /// <param name="model">The image embedding model.</param>
    /// <returns>The text embedder with the same model identity that runs on this platform, or <c>null</c>.</returns>
    ITextEmbeddingProvider? FindTextEmbedderFor(ModelIdentity model);

    /// <summary>
    /// Checks the health of every provider. Providers that don't run on this platform report
    /// <see cref="ProviderHealthStatus.NotSupportedOnPlatform"/> without being called.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One status per provider, in registration order.</returns>
    Task<IReadOnlyList<ProviderStatus>> CheckHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks the health of one provider.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    /// <exception cref="KeyNotFoundException">No provider has the ID.</exception>
    Task<ProviderStatus> CheckHealthAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the result of the last health check of a provider.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    /// <returns>The last status, or <c>null</c> if it hasn't been checked since it was registered.</returns>
    ProviderStatus? GetLastHealth(string providerId);
}
