using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.AI.Benchmarks;

/// <summary>
/// Keeps the latest benchmark report in <c>benchmark.json</c> in the app's data folder, apart from the settings (it's a
/// measurement of this machine, not a preference), and turns it into the throughput the progress estimate starts from
/// (E06-S08-T04). A missing or unreadable file means no report.
/// </summary>
public sealed class BenchmarkResultStore : IBenchmarkResults, IThroughputHints
{
    /// <summary>
    /// The job type the throughput hint applies to; the analysis pipeline's job type (E06-S01).
    /// </summary>
    public const string AnalysisJobType = "analysis";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;
    private readonly ILogger<BenchmarkResultStore> logger;
    private readonly OnnxSessionFactory sessionFactory;
    private readonly Siglip2Options options;
    private readonly Lock gate = new();
    private BenchmarkReport? latest;
    private bool loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkResultStore"/> class.
    /// </summary>
    /// <param name="appPaths">Where the app keeps its data.</param>
    /// <param name="sessionFactory">The session factory, for the execution provider chosen from the hardware.</param>
    /// <param name="options">The SigLIP 2 settings, for an overridden execution provider or tier.</param>
    /// <param name="logger">The logger.</param>
    public BenchmarkResultStore(IAppPaths appPaths, OnnxSessionFactory sessionFactory, Siglip2Options options, ILogger<BenchmarkResultStore> logger)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        path = Path.Combine(appPaths.DataDirectory, "benchmark.json");
        this.sessionFactory = sessionFactory;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    public BenchmarkReport? Latest
    {
        get
        {
            lock (gate)
            {
                if (!loaded)
                {
                    latest = Read();
                    loaded = true;
                }

                return latest;
            }
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(BenchmarkReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        lock (gate)
        {
            latest = report;
            loaded = true;
        }
    }

    /// <inheritdoc />
    public double? ItemsPerSecond(string jobType)
    {
        if (jobType != AnalysisJobType || Latest is not { } report)
        {
            return null;
        }

        var provider = options.ExecutionProvider ?? sessionFactory.DefaultExecutionProvider;
        var entry = report.Find(Siglip2Benchmark.ImageAnalyzer, provider, options.TierId)
            ?? report.Find(Siglip2Benchmark.ImageAnalyzer, provider)
            ?? report.Find(Siglip2Benchmark.ImageAnalyzer, ExecutionProvider.Cpu);
        return entry is { ItemsPerSecond: > 0 } ? entry.ItemsPerSecond : null;
    }

    private BenchmarkReport? Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Couldn't read the benchmark results in {Path}; ignoring them", path);
            return null;
        }
    }
}
