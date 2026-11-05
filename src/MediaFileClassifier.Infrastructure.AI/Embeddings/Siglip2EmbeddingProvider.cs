using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// The SigLIP 2 embedding provider (D092): images and texts into one 768-dimension space. It's always registered;
/// it finds the model in the <see cref="IModelStore"/> on first use, so a model installed later (E06-S12) is picked
/// up without a restart. Until then its health is <see cref="ProviderHealthStatus.ModelMissing"/> and calls throw
/// <see cref="AiFailureKind.ModelMissing"/>, which the analysis queue treats as a waiting state (D037).
/// </summary>
public sealed class Siglip2EmbeddingProvider : IImageEmbeddingProvider, ITextEmbeddingProvider, IPreloadableAiProvider, IDisposable
{
    private static readonly ModelIdentity NotInstalledIdentity = Siglip2OnnxModel.CreateIdentity(null);

    private readonly IModelStore store;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly Siglip2Options options;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private volatile Siglip2OnnxModel? model;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Siglip2EmbeddingProvider"/> class.
    /// </summary>
    /// <param name="store">Model store the files are installed in.</param>
    /// <param name="sessionFactory">ONNX session factory.</param>
    /// <param name="options">Batch size and execution provider.</param>
    public Siglip2EmbeddingProvider(IModelStore store, OnnxSessionFactory sessionFactory, Siglip2Options options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ImageBatchSize, nameof(options));

        this.store = store;
        this.sessionFactory = sessionFactory;
        this.options = options;
    }

    /// <inheritdoc />
    public string Id => Siglip2Model.ProviderId;

    /// <inheritdoc />
    public string DisplayName => "SigLIP 2";

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.Onnx;

    /// <summary>
    /// Gets the installed model's identity, with its content hash once the model has been found; before that, the
    /// identity without a hash.
    /// </summary>
    public ModelIdentity Model => model?.Model ?? NotInstalledIdentity;

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform => true;

    /// <inheritdoc />
    public int Dimension => Siglip2Model.Dimension;

    /// <inheritdoc />
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var current = await FindModelAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new ProviderHealth(
                ProviderHealthStatus.ModelMissing,
                "The SigLIP 2 model isn't installed. Download it or import its files in the model settings.",
                $"{Siglip2Model.ModelId} {Siglip2Model.Version} isn't in {store.RootDirectory}.");
        }

        return await current.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        return CallAsync(current => current.EmbedImagesAsync(images, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return CallAsync(current => current.EmbedTextsAsync(texts, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task PreloadAsync(AiCapability? capability, CancellationToken cancellationToken) =>
        CallAsync(
            current => Task.Run(
                () =>
                {
                    current.Load(capability);
                    return true;
                },
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public void Unload()
    {
        // A call that already holds the old model finishes on it, or retries on a new one if it was disposed.
        var old = Interlocked.Exchange(ref model, null);
        old?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        model?.Dispose();
        model = null;
        loadGate.Dispose();
    }

    private async Task<T> CallAsync<T>(Func<Siglip2OnnxModel, Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var current = await FindModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AiProviderException(AiFailureKind.ModelMissing, Id, "The SigLIP 2 model isn't installed.");
            try
            {
                return await call(current).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (attempt == 0 && !disposed)
            {
                // Unloaded while this call ran: load it again once.
            }
        }
    }

    private async Task<Siglip2OnnxModel?> FindModelAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (model is { } loaded)
        {
            return loaded;
        }

        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (model is { } raced)
            {
                return raced;
            }

            var installed = await store.FindAsync(Siglip2Model.ModelId, Siglip2Model.Version, cancellationToken).ConfigureAwait(false);
            if (installed is null)
            {
                return null;
            }

            model = new Siglip2OnnxModel(installed, sessionFactory, options.ExecutionProvider, options.ImageBatchSize);
            return model;
        }
        finally
        {
            loadGate.Release();
        }
    }
}
