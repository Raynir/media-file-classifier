namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// A configured AI provider: one model behind one way of running it. A provider implements one or more capability
/// interfaces (<see cref="IImageDescriber"/>, <see cref="IOcrProvider"/>, <see cref="ITranscriptionProvider"/>,
/// <see cref="IImageEmbeddingProvider"/>, <see cref="ITextEmbeddingProvider"/>), and the pipeline finds it through
/// the provider registry by capability, so a new provider needs no pipeline changes.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Gets the ID of this provider instance, unique within the registry, e.g. <c>onnx:siglip2-base-patch16-256</c>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name shown to the user.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets how the provider runs its model.
    /// </summary>
    AiProviderKind Kind { get; }

    /// <summary>
    /// Gets the model the provider runs. Every output carries it.
    /// </summary>
    ModelIdentity Model { get; }

    /// <summary>
    /// Gets a value indicating whether the provider can run on this platform at all, e.g. a native library that only
    /// exists for Windows. The registry never offers an unsupported provider.
    /// </summary>
    bool IsSupportedOnCurrentPlatform { get; }

    /// <summary>
    /// Checks whether the provider can work now: the endpoint is reachable, the model file is present, or the native
    /// library loads. Never throws for an unhealthy provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider's health.</returns>
    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);
}
