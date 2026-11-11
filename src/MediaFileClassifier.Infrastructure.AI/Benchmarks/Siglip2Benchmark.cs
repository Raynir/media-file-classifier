using System.Diagnostics;
using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.AI.Benchmarks;

/// <summary>
/// Times SigLIP 2's image and text towers on generated samples (E06-S08-T04): every installed tier, on the CPU and on
/// the GPU the hardware prefers. The samples are fixed (a seeded generator), so runs on different machines compare.
/// </summary>
/// <remarks>
/// A run is bounded: <see cref="ImageCount"/> images and <see cref="TextCount"/> texts per entry, each tower stopping at
/// <see cref="BudgetPerTower"/>. On the CPU at about 15 images/s a tier takes around 5 s, so a machine with two tiers
/// and a GPU finishes well inside two minutes.
/// </remarks>
public sealed class Siglip2Benchmark : IBenchmarkRunner
{
    /// <summary>
    /// The analyzer name of the image tower in a <see cref="BenchmarkEntry"/>.
    /// </summary>
    public const string ImageAnalyzer = "siglip2.image";

    /// <summary>
    /// The analyzer name of the text tower in a <see cref="BenchmarkEntry"/>.
    /// </summary>
    public const string TextAnalyzer = "siglip2.text";

    /// <summary>
    /// How many generated images each entry times.
    /// </summary>
    public const int ImageCount = 24;

    /// <summary>
    /// How many generated texts each entry times.
    /// </summary>
    public const int TextCount = 64;

    private const int Width = 640;
    private const int Height = 480;
    private const int Seed = 86;

    private static readonly TimeSpan BudgetPerTower = TimeSpan.FromSeconds(30);

    private readonly IModelStore store;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly IHardwareInfo hardware;
    private readonly IBenchmarkResults results;
    private readonly Siglip2Options options;
    private readonly TimeProvider clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="Siglip2Benchmark"/> class.
    /// </summary>
    /// <param name="store">The model store the tiers are installed in.</param>
    /// <param name="sessionFactory">The ONNX session factory.</param>
    /// <param name="hardware">The detected hardware.</param>
    /// <param name="results">Where the report is saved.</param>
    /// <param name="options">The SigLIP 2 settings, for the batch size.</param>
    /// <param name="clock">The clock.</param>
    public Siglip2Benchmark(
        IModelStore store,
        OnnxSessionFactory sessionFactory,
        IHardwareInfo hardware,
        IBenchmarkResults results,
        Siglip2Options options,
        TimeProvider clock)
    {
        this.store = store;
        this.sessionFactory = sessionFactory;
        this.hardware = hardware;
        this.results = results;
        this.options = options;
        this.clock = clock;
    }

    /// <summary>
    /// Describes the processors, memory and adapters of a machine for a report.
    /// </summary>
    /// <param name="profile">The detected hardware.</param>
    /// <returns>A one-line description.</returns>
    public static string Describe(HardwareProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var gpus = profile.Gpus.Count == 0 ? "no GPU" : string.Join(", ", profile.Gpus.Select(gpu => gpu.Name));
        return string.Create(CultureInfo.InvariantCulture, $"{profile.LogicalProcessorCount} logical processors, {profile.TotalMemoryBytes / (1024.0 * 1024 * 1024):0.0} GB memory, {gpus}");
    }

    /// <inheritdoc />
    public async Task<BenchmarkReport> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var tiers = await store.FindTiersAsync(Siglip2Model.ModelId, Siglip2Model.Version, cancellationToken).ConfigureAwait(false);
        var providers = hardware.PreferredExecutionProvider == ExecutionProvider.Cpu
            ? new[] { ExecutionProvider.Cpu }
            : [ExecutionProvider.Cpu, hardware.PreferredExecutionProvider];
        var entries = new List<BenchmarkEntry>();
        if (tiers.Count == 0)
        {
            progress?.Report("The SigLIP 2 model isn't installed, so there is nothing to time. Install it in the model settings first.");
        }

        var images = tiers.Count == 0 ? [] : CreateImages();
        var texts = Enumerable.Range(0, TextCount).Select(i => $"a photo of object number {i} on a table in daylight").ToList();
        foreach (var tier in tiers)
        {
            foreach (var requested in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Timing {tier.TierId} on {requested}...");
                var measured = await Task.Run(() => Measure(tier, requested, images, texts, cancellationToken), cancellationToken).ConfigureAwait(false);
                foreach (var entry in measured)
                {
                    progress?.Report($"  {entry.Analyzer}: {entry.ItemsPerSecond:0.0} items/s on {entry.Provider} ({entry.Items} items, load {entry.LoadSeconds:0.0} s)");
                }

                entries.AddRange(measured);
            }
        }

        var report = new BenchmarkReport(clock.GetUtcNow(), Describe(hardware.Profile), entries);
        if (entries.Count > 0)
        {
            await results.SaveAsync(report, cancellationToken).ConfigureAwait(false);
        }

        return report;
    }

    private static List<AiImage> CreateImages()
    {
        var random = new Random(Seed);
        var images = new List<AiImage>(ImageCount);
        for (var i = 0; i < ImageCount; i++)
        {
            var pixels = new byte[Width * Height * 4];
            random.NextBytes(pixels);
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = 255;
            }

            images.Add(AiImage.FromRgba(pixels, Width, Height, $"benchmark-{i}"));
        }

        return images;
    }

    private List<BenchmarkEntry> Measure(InstalledModel tier, ExecutionProvider requested, List<AiImage> images, List<string> texts, CancellationToken cancellationToken)
    {
        using var model = new Siglip2OnnxModel(tier, sessionFactory, requested, options.ImageBatchSize);

        // Load both towers and run each once, so the timed part has no session creation and no first-run cost.
        var loading = Stopwatch.StartNew();
        model.Load(null);
        model.EmbedImagesAsync(images.Take(1).ToList(), cancellationToken).GetAwaiter().GetResult();
        model.EmbedTextsAsync(texts.Take(1).ToList(), cancellationToken).GetAwaiter().GetResult();
        var loadSeconds = loading.Elapsed.TotalSeconds;

        var (imageCount, imageSeconds) = Time(images, batch => model.EmbedImagesAsync(batch, cancellationToken).GetAwaiter().GetResult(), options.ImageBatchSize, cancellationToken);
        var (textCount, textSeconds) = Time(texts, batch => model.EmbedTextsAsync(batch, cancellationToken).GetAwaiter().GetResult(), 16, cancellationToken);
        return
        [
            new(ImageAnalyzer, tier.TierId, model.ExecutionProviderOf(Siglip2Model.VisionRole), requested, imageCount / imageSeconds, imageCount, loadSeconds),
            new(TextAnalyzer, tier.TierId, model.ExecutionProviderOf(Siglip2Model.TextRole), requested, textCount / textSeconds, textCount, loadSeconds),
        ];
    }

    private static (int Count, double Seconds) Time<T>(List<T> items, Action<List<T>> embed, int batchSize, CancellationToken cancellationToken)
    {
        var count = 0;
        var watch = Stopwatch.StartNew();
        foreach (var batch in items.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            embed([.. batch]);
            count += batch.Length;
            if (watch.Elapsed >= BudgetPerTower)
            {
                break;
            }
        }

        return (count, Math.Max(watch.Elapsed.TotalSeconds, 0.001));
    }
}
