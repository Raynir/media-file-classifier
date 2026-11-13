using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.AI.Benchmarks;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Benchmarks;

/// <summary>
/// Tests for the benchmark's result store, the throughput hint it gives the progress estimate, and the runner when
/// there is nothing to time (E06-S08-T04). Timing a real model needs the SigLIP 2 files, which CI doesn't have.
/// </summary>
public sealed class BenchmarkTests : IDisposable
{
    private static readonly DateTimeOffset Moment = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly TemporaryFolder temp = new();
    private readonly string directory;

    public BenchmarkTests()
    {
        directory = temp.Combine("benchmark");
    }

    public void Dispose()
    {
        temp.Dispose();
    }

    [Fact]
    public async Task Report_IsSavedAndReadBackByANewStore()
    {
        var report = Report(Entry(Siglip2Benchmark.ImageAnalyzer, "standard", ExecutionProvider.Cpu, 14.5));

        await NewStore().SaveAsync(report, CancellationToken.None);

        var read = NewStore().Latest;
        Assert.NotNull(read);
        Assert.Equal(report.Machine, read.Machine);
        Assert.Equal(report.MeasuredAt, read.MeasuredAt);
        Assert.Equal(report.Entries, read.Entries);
        Assert.Contains("\"Cpu\"", File.ReadAllText(Path.Combine(directory, "benchmark.json")));
    }

    [Fact]
    public void NoFile_MeansNoReport_AndNoHint()
    {
        var store = NewStore();

        Assert.Null(store.Latest);
        Assert.Null(store.ItemsPerSecond(BenchmarkResultStore.AnalysisJobType));
    }

    [Fact]
    public void UnreadableFile_IsIgnored()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "benchmark.json"), "{ not json");

        Assert.Null(NewStore().Latest);
    }

    [Fact]
    public async Task Hint_UsesTheImageTowerOfTheProviderInUse_AndFallsBackToTheCpuFigure()
    {
        var report = Report(
            Entry(Siglip2Benchmark.ImageAnalyzer, "standard", ExecutionProvider.Cpu, 14),
            Entry(Siglip2Benchmark.ImageAnalyzer, "compact", ExecutionProvider.DirectMl, 60),
            Entry(Siglip2Benchmark.TextAnalyzer, "compact", ExecutionProvider.DirectMl, 500));
        await NewStore().SaveAsync(report, CancellationToken.None);

        Assert.Equal(14, NewStore(new Siglip2Options { ExecutionProvider = ExecutionProvider.Cpu }).ItemsPerSecond("analysis"));
        Assert.Equal(60, NewStore(new Siglip2Options { ExecutionProvider = ExecutionProvider.DirectMl }).ItemsPerSecond("analysis"));
        Assert.Equal(14, NewStore(new Siglip2Options { ExecutionProvider = ExecutionProvider.Cpu, TierId = "standard" }).ItemsPerSecond("analysis"));

        // The configured tier wasn't timed on that provider: the provider's best figure stands in.
        Assert.Equal(60, NewStore(new Siglip2Options { ExecutionProvider = ExecutionProvider.DirectMl, TierId = "standard" }).ItemsPerSecond("analysis"));

        // Nothing measured on CUDA: the CPU figure is the safe one.
        Assert.Equal(14, NewStore(new Siglip2Options { ExecutionProvider = ExecutionProvider.Cuda }).ItemsPerSecond("analysis"));
    }

    [Fact]
    public async Task Hint_IsOnlyForAnalysisJobs()
    {
        await NewStore().SaveAsync(Report(Entry(Siglip2Benchmark.ImageAnalyzer, "standard", ExecutionProvider.Cpu, 14)), CancellationToken.None);

        Assert.Null(NewStore().ItemsPerSecond("import"));
    }

    [Fact]
    public async Task Runner_WithNoModelInstalled_SaysSo_TimesNothing_AndKeepsTheOldReport()
    {
        var results = NewStore();
        var old = Report(Entry(Siglip2Benchmark.ImageAnalyzer, "standard", ExecutionProvider.Cpu, 14));
        await results.SaveAsync(old, CancellationToken.None);
        var lines = new List<string>();
        var runner = new Siglip2Benchmark(new Siglip2StubStore(null), new OnnxSessionFactory(), new FakeHardwareInfo(), results, new Siglip2Options(), TimeProvider.System);

        var report = await runner.RunAsync(new Lines(lines), CancellationToken.None);

        Assert.Empty(report.Entries);
        Assert.Same(old, results.Latest);
        Assert.Contains(lines, line => line.Contains("isn't installed", StringComparison.Ordinal));
    }

    [Fact]
    public void Describe_NamesTheProcessorsMemoryAndAdapters()
    {
        var profile = new HardwareProfile("X64", 16, 32L << 30, [new GpuInfo("RTX 4070", "NVIDIA", 8L << 30)]);

        Assert.Equal("16 logical processors, 32.0 GB memory, RTX 4070", Siglip2Benchmark.Describe(profile));
        Assert.EndsWith("no GPU", Siglip2Benchmark.Describe(profile with { Gpus = [] }), StringComparison.Ordinal);
    }

    private static BenchmarkReport Report(params BenchmarkEntry[] entries) => new(Moment, "8 logical processors", entries);

    private static BenchmarkEntry Entry(string analyzer, string tier, ExecutionProvider provider, double rate) =>
        new(analyzer, tier, provider, provider, rate, 24, 1.5);

    private BenchmarkResultStore NewStore(Siglip2Options? options = null) =>
        new(new Paths(directory), new OnnxSessionFactory(), options ?? new Siglip2Options(), NullLogger<BenchmarkResultStore>.Instance);

    private sealed class Paths(string data) : IAppPaths
    {
        public string DataDirectory => data;

        public string CacheDirectory => Path.Combine(data, "cache");

        public string LogsDirectory => Path.Combine(data, "logs");

        public string SettingsFile => Path.Combine(data, "settings.json");
    }

    private sealed class Lines(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }
}
