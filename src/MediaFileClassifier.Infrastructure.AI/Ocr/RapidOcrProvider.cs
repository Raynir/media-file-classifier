using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// The in-process OCR provider (D347): PaddleOCR's PP-OCRv5 models run with ONNX Runtime, as RapidOCR runs them, so
/// text is read without a local server, on any head. Like the SigLIP 2 provider it's always registered and finds the
/// model in the <see cref="IModelStore"/> on first use; until the model is installed its health is
/// <see cref="ProviderHealthStatus.ModelMissing"/> and calls throw <see cref="AiFailureKind.ModelMissing"/> (D037).
/// </summary>
public sealed class RapidOcrProvider : IOcrProvider, ITextDetector, IPreloadableAiProvider, IDisposable
{
    private static readonly ModelIdentity NotInstalledIdentity = RapidOcrOnnxModel.CreateIdentity(null);

    private readonly IModelStore store;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly RapidOcrOptions options;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly List<RapidOcrOnnxModel> retired = [];
    private volatile RapidOcrOnnxModel? model;
    private volatile bool storeChanged = true;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RapidOcrProvider"/> class.
    /// </summary>
    /// <param name="store">Model store the files are installed in.</param>
    /// <param name="sessionFactory">ONNX session factory.</param>
    /// <param name="options">Thresholds, sizes and execution provider.</param>
    public RapidOcrProvider(IModelStore store, OnnxSessionFactory sessionFactory, RapidOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RecognitionBatchSize, nameof(options));

        this.store = store;
        this.sessionFactory = sessionFactory;
        this.options = options;
        store.ModelsChanged += OnModelsChanged;
    }

    /// <inheritdoc />
    public string Id => RapidOcrModel.ProviderId;

    /// <inheritdoc />
    public string DisplayName => "PP-OCRv5 (in-process)";

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.Onnx;

    /// <summary>
    /// Gets the installed model's identity, with its content hash once the model has been found.
    /// </summary>
    public ModelIdentity Model => model?.Model ?? NotInstalledIdentity;

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform => true;

    /// <inheritdoc />
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var current = await FindModelAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new ProviderHealth(
                ProviderHealthStatus.ModelMissing,
                "The in-process OCR model isn't installed. Download it or import its files in the model settings.",
                $"{RapidOcrModel.ModelId} {RapidOcrModel.Version} isn't in {store.RootDirectory}.");
        }

        return await current.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CallAsync(current => current.RecognizeTextAsync(image, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var boxes = await CallAsync(current => current.DetectAsync(image, cancellationToken), cancellationToken).ConfigureAwait(false);
        return boxes.Value.Count > 0;
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

    /// <summary>
    /// Runs a call on the installed model.
    /// </summary>
    /// <typeparam name="T">Type of the result.</typeparam>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    internal async Task<T> CallAsync<T>(Func<RapidOcrOnnxModel, Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var current = await FindModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AiProviderException(AiFailureKind.ModelMissing, Id, "The in-process OCR model isn't installed.");
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

    private async Task<RapidOcrOnnxModel?> FindModelAsync(CancellationToken cancellationToken)
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
            var installed = (await store.FindTiersAsync(RapidOcrModel.ModelId, RapidOcrModel.Version, cancellationToken).ConfigureAwait(false))
                .OrderBy(tier => tier.TierId, StringComparer.Ordinal)
                .FirstOrDefault();
            var current = model;
            if (current is not null && installed is not null && current.Model.FileHash == installed.ContentHash.ToLowerInvariant())
            {
                return current;
            }

            // A call may still run on the old model, so it's released with the provider.
            var provider = options.ExecutionProvider ?? sessionFactory.DefaultExecutionProvider;
            model = installed is null ? null : new RapidOcrOnnxModel(installed, sessionFactory, provider, options);
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
