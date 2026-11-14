namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// What the benchmark measured for one analyzer on one model tier and execution provider (E06-S08-T04).
/// </summary>
/// <param name="Analyzer">The analyzer, e.g. <c>siglip2.image</c> or <c>siglip2.text</c>.</param>
/// <param name="TierId">The model tier that was timed.</param>
/// <param name="Provider">The execution provider the sessions really ran on, which is the CPU when the GPU one failed.</param>
/// <param name="RequestedProvider">The execution provider the benchmark asked for.</param>
/// <param name="ItemsPerSecond">Items (images or texts) embedded per second, after the model loaded and warmed up.</param>
/// <param name="Items">How many items were timed.</param>
/// <param name="LoadSeconds">Seconds the sessions took to load and run once.</param>
public sealed record BenchmarkEntry(
    string Analyzer,
    string TierId,
    ExecutionProvider Provider,
    ExecutionProvider RequestedProvider,
    double ItemsPerSecond,
    int Items,
    double LoadSeconds);

/// <summary>
/// The results of one benchmark run on this machine.
/// </summary>
/// <param name="MeasuredAt">When the run finished.</param>
/// <param name="Machine">A short description of the processor count, memory and graphics adapters it ran on.</param>
/// <param name="Entries">The measurements.</param>
public sealed record BenchmarkReport(DateTimeOffset MeasuredAt, string Machine, IReadOnlyList<BenchmarkEntry> Entries)
{
    /// <summary>
    /// Gets the fastest entry of an analyzer on an execution provider.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="provider">The execution provider the sessions ran on.</param>
    /// <param name="tierId">Only this tier, or <see langword="null"/> for any.</param>
    /// <returns>The entry, or <see langword="null"/> when none matches.</returns>
    public BenchmarkEntry? Find(string analyzer, ExecutionProvider provider, string? tierId = null) =>
        Entries
            .Where(entry => entry.Analyzer == analyzer && entry.Provider == provider && (tierId is null || entry.TierId == tierId))
            .MaxBy(entry => entry.ItemsPerSecond);
}

/// <summary>
/// Keeps the latest benchmark report of this machine (E06-S08-T04).
/// </summary>
public interface IBenchmarkResults
{
    /// <summary>
    /// Gets the latest report, or <see langword="null"/> when the benchmark has never run.
    /// </summary>
    BenchmarkReport? Latest { get; }

    /// <summary>
    /// Stores a report as the latest.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that ends when the report is saved.</returns>
    Task SaveAsync(BenchmarkReport report, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the benchmark: times each analyzer on a fixed generated sample set, per installed model tier and execution provider.
/// </summary>
public interface IBenchmarkRunner
{
    /// <summary>
    /// Runs the benchmark and stores the report.
    /// </summary>
    /// <param name="progress">Receives one line per step, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report; its entries are empty when no model is installed.</returns>
    Task<BenchmarkReport> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken);
}
