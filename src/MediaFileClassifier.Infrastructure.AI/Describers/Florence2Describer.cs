using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.AI.Describers;

/// <summary>
/// The in-process describer (E06-S04-T01): Florence-2 run with ONNX Runtime on the CPU, so items get a description,
/// a summary, objects and tags without a server. One instance per model size; like the other in-process providers it's
/// always registered and finds its model in the <see cref="IModelStore"/> on first use, and until the model is
/// installed its health is <see cref="ProviderHealthStatus.ModelMissing"/> and calls throw
/// <see cref="AiFailureKind.ModelMissing"/> (D037). It takes one picture per request, so a clip is described frame by
/// frame and merged (E06-S04-T07).
/// </summary>
public sealed class Florence2Describer : IImageDescriber, IPreloadableAiProvider, IDisposable
{
    private readonly Florence2Variant variant;
    private readonly IModelStore store;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly Florence2Options options;
    private readonly ModelIdentity notInstalledIdentity;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly List<Florence2OnnxModel> retired = [];
    private volatile Florence2OnnxModel? model;
    private volatile bool storeChanged = true;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Florence2Describer"/> class.
    /// </summary>
    /// <param name="variant">The model size, <see cref="Florence2Model.Base"/> or <see cref="Florence2Model.Large"/>.</param>
    /// <param name="store">Model store the files are installed in.</param>
    /// <param name="sessionFactory">ONNX session factory.</param>
    /// <param name="options">Token limits.</param>
    public Florence2Describer(Florence2Variant variant, IModelStore store, OnnxSessionFactory sessionFactory, Florence2Options options)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(options);

        this.variant = variant;
        this.store = store;
        this.sessionFactory = sessionFactory;
        this.options = options;
        notInstalledIdentity = Florence2OnnxModel.CreateIdentity(variant, null);
        store.ModelsChanged += OnModelsChanged;
    }

    /// <inheritdoc />
    public string Id => variant.ProviderId;

    /// <inheritdoc />
    public string DisplayName => variant.DisplayName;

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.Onnx;

    /// <summary>
    /// Gets the installed model's identity, with its content hash once the model has been found.
    /// </summary>
    public ModelIdentity Model => model?.Model ?? notInstalledIdentity;

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform => true;

    /// <inheritdoc />
    public string PromptVersion => Florence2Model.PromptVersion;

    /// <inheritdoc />
    public DescriberFields SupportedFields => Florence2Model.SupportedFields;

    /// <inheritdoc />
    public string GetPromptVersion(string? requested) => Florence2Model.PromptVersion;

    /// <inheritdoc />
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var current = await FindModelAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new ProviderHealth(
                ProviderHealthStatus.ModelMissing,
                $"{variant.DisplayName} isn't installed. Download it or import its files in the model settings.",
                $"{variant.ModelId} {Florence2Model.Version} isn't in {store.RootDirectory}.");
        }

        return await current.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        return CallAsync(current => current.DescribeAsync(image, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task PreloadAsync(AiCapability? capability, CancellationToken cancellationToken) =>
        CallAsync(
            current => Task.Run(
                () =>
                {
                    current.Load();
                    return true;
                },
                cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public void Unload()
    {
        var old = Interlocked.Exchange(ref model, null);
        storeChanged = true;
        old?.Dispose();
        DisposeRetired();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        store.ModelsChanged -= OnModelsChanged;
        model?.Dispose();
        model = null;
        DisposeRetired();
        loadGate.Dispose();
    }

    private async Task<T> CallAsync<T>(Func<Florence2OnnxModel, Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var current = await FindModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AiProviderException(AiFailureKind.ModelMissing, Id, $"{variant.DisplayName} isn't installed.");
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

    private void DisposeRetired()
    {
        lock (retired)
        {
            foreach (var old in retired)
            {
                old.Dispose();
            }

            retired.Clear();
        }
    }

    private void OnModelsChanged(object? sender, EventArgs e) => storeChanged = true;

    private async Task<Florence2OnnxModel?> FindModelAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (model is { } loaded && !storeChanged)
        {
            return loaded;
        }

        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (model is { } raced && !storeChanged)
            {
                return raced;
            }

            storeChanged = false;
            var installed = (await store.FindTiersAsync(variant.ModelId, Florence2Model.Version, cancellationToken).ConfigureAwait(false))
                .OrderBy(tier => tier.TierId, StringComparer.Ordinal)
                .FirstOrDefault();
            var current = model;
            if (current is not null && installed is not null && current.Model.FileHash == installed.ContentHash.ToLowerInvariant())
            {
                return current;
            }

            // A call may still run on the old model, so it's released with the provider.
            model = installed is null ? null : new Florence2OnnxModel(variant, installed, sessionFactory, options);
            if (current is not null)
            {
                lock (retired)
                {
                    retired.Add(current);
                }
            }

            return model;
        }
        catch
        {
            storeChanged = true;
            throw;
        }
        finally
        {
            loadGate.Release();
        }
    }
}
