using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// The SigLIP 2 embedding provider (D092): images and texts into one 768-dimension space. It's always registered;
/// it finds the model in the <see cref="IModelStore"/> on first use, so a model installed later (E06-S12) is picked
/// up without a restart. Until then its health is <see cref="ProviderHealthStatus.ModelMissing"/> and calls throw
/// <see cref="AiFailureKind.ModelMissing"/>, which the analysis queue treats as a waiting state (D037).
/// </summary>
/// <remarks>
/// When several tiers are installed (E06-S08-T03), it uses <see cref="Siglip2Options.TierId"/> if that one is installed,
/// else the tier that suits the execution provider (fp32 <c>standard</c> on the CPU, <c>compact</c> on a GPU), else the
/// first installed tier in catalogue order. A change in the store makes it choose again on the next call.
/// </remarks>
public sealed class Siglip2EmbeddingProvider : IImageEmbeddingProvider, ITextEmbeddingProvider, IPreloadableAiProvider, IDisposable
{
    private static readonly ModelIdentity NotInstalledIdentity = Siglip2OnnxModel.CreateIdentity(null);

    private readonly IModelStore store;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly Siglip2Options options;
    private readonly IModelCatalog catalog;
    private readonly IHardwareOverrides? overrides;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly List<Siglip2OnnxModel> retired = [];
    private volatile Siglip2OnnxModel? model;
    private volatile bool storeChanged;
    private volatile bool onCpu;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Siglip2EmbeddingProvider"/> class.
    /// </summary>
    /// <param name="store">Model store the files are installed in.</param>
    /// <param name="sessionFactory">ONNX session factory.</param>
    /// <param name="options">Batch size, execution provider and tier.</param>
    /// <param name="catalog">The model catalogue, for the tiers' order and the hardware they suit; the embedded one if <see langword="null"/>.</param>
    /// <param name="overrides">
    /// The user's execution provider and tier (E17-S08-T02), which win over <paramref name="options"/>; a change releases
    /// the loaded model so the next call uses them. <see langword="null"/> where there are no hardware settings.
    /// </param>
    public Siglip2EmbeddingProvider(
        IModelStore store,
        OnnxSessionFactory sessionFactory,
        Siglip2Options options,
        IModelCatalog? catalog = null,
        IHardwareOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ImageBatchSize, nameof(options));

        this.store = store;
        this.sessionFactory = sessionFactory;
        this.options = options;
        this.catalog = catalog ?? new ModelCatalog();
        store.ModelsChanged += OnModelsChanged;
        this.overrides = overrides;
        if (overrides is not null)
        {
            overrides.Changed += OnOverridesChanged;
        }
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
        DisposeRetired();
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        store.ModelsChanged -= OnModelsChanged;
        if (overrides is not null)
        {
            overrides.Changed -= OnOverridesChanged;
        }

        model?.Dispose();
        model = null;
        DisposeRetired();
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
            catch (AiProviderException ex) when (attempt == 0 && ex.Kind == AiFailureKind.ProviderError && !disposed && !onCpu)
            {
                // A GPU tier can fail where the CPU can't run it, e.g. the fp16 text model doesn't load on the CPU.
                if (!await SwitchToCpuTierAsync(current, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Chooses among the installed tiers of the model: the configured one, else the one that suits the execution
    /// provider, else the first in catalogue order (tiers the catalogue doesn't list come last).
    /// </summary>
    /// <param name="installed">The installed tiers.</param>
    /// <param name="catalog">The model catalogue.</param>
    /// <param name="provider">The execution provider sessions run on.</param>
    /// <param name="preferredTierId">The configured tier, if any.</param>
    /// <returns>The tier to use, or <see langword="null"/> if none is installed.</returns>
    internal static InstalledModel? ChooseTier(IReadOnlyList<InstalledModel> installed, IModelCatalog catalog, ExecutionProvider provider, string? preferredTierId)
    {
        if (installed.FirstOrDefault(tier => tier.TierId == preferredTierId) is { } configured)
        {
            return configured;
        }

        var tiers = catalog.Find(Siglip2Model.ModelId)?.Tiers ?? [];
        var order = tiers.Select(tier => tier.TierId).ToList();
        var ordered = installed
            .OrderBy(model => order.IndexOf(model.TierId) is var index and >= 0 ? index : int.MaxValue)
            .ThenBy(model => model.TierId, StringComparer.Ordinal)
            .ToList();
        return ordered.FirstOrDefault(model => tiers.FirstOrDefault(tier => tier.TierId == model.TierId)?.Suits(provider) == true)
            ?? ordered.FirstOrDefault();
    }

    /// <summary>
    /// When a tier meant for the GPU fails and another installed tier suits the CPU, uses the CPU from now on: the next
    /// call chooses again, and picks that tier. A restart tries the GPU again.
    /// </summary>
    private async Task<bool> SwitchToCpuTierAsync(Siglip2OnnxModel failed, CancellationToken cancellationToken)
    {
        var installed = await store.FindTiersAsync(Siglip2Model.ModelId, Siglip2Model.Version, cancellationToken).ConfigureAwait(false);
        var tiers = catalog.Find(Siglip2Model.ModelId)?.Tiers ?? [];
        var current = installed.FirstOrDefault(tier => tier.ContentHash.Equals(failed.Model.FileHash, StringComparison.OrdinalIgnoreCase));
        var currentSuitsCpu = current is not null && tiers.FirstOrDefault(tier => tier.TierId == current.TierId)?.Suits(ExecutionProvider.Cpu) == true;
        if (currentSuitsCpu || ChooseTier(installed, catalog, ExecutionProvider.Cpu, null) is not { } replacement || replacement.TierId == current?.TierId)
        {
            return false;
        }

        onCpu = true;
        storeChanged = true;
        return true;
    }

    private void OnModelsChanged(object? sender, EventArgs e) => storeChanged = true;

    /// <summary>
    /// The user chose another execution provider or tier: they apply from the next call. The choice is the user's, so
    /// a GPU tier that failed before is tried again (the session factory still keeps a file that failed on the GPU on
    /// the CPU until restart).
    /// </summary>
    private void OnOverridesChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        onCpu = false;
        storeChanged = true;
        Unload();
    }

    private async Task<Siglip2OnnxModel?> FindModelAsync(CancellationToken cancellationToken)
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
            var installed = await store.FindTiersAsync(Siglip2Model.ModelId, Siglip2Model.Version, cancellationToken).ConfigureAwait(false);
            var provider = onCpu ? ExecutionProvider.Cpu : overrides?.ExecutionProvider ?? options.ExecutionProvider ?? sessionFactory.DefaultExecutionProvider;
            var chosen = ChooseTier(installed, catalog, provider, overrides?.TierId ?? options.TierId);
            var current = model;
            if (current is not null
                && chosen is not null
                && current.Model.FileHash == chosen.ContentHash.ToLowerInvariant()
                && current.PreferredExecutionProvider == provider)
            {
                // The same tier is still the best one: keep the loaded sessions.
                return current;
            }

            // Calls may still be running on the old model, and a native session freed under a running call can crash the
            // process, so the old one is kept until this provider is disposed (tier swaps are rare).
            model = chosen is null ? null : new Siglip2OnnxModel(chosen, sessionFactory, provider, options.ImageBatchSize);
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
            // Couldn't look at the store: choose again next time.
            storeChanged = true;
            throw;
        }
        finally
        {
            loadGate.Release();
        }
    }
}
