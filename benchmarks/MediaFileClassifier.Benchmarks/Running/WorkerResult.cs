using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Measurement;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// What one worker process measured for one engine on one library.
/// </summary>
internal sealed record WorkerResult(
    string Engine,
    string Library,
    double LoadMs,
    double FirstQueryMs,
    MemorySnapshot Baseline,
    MemorySnapshot AfterLoad,
    MemorySnapshot AfterQueries,
    IReadOnlyList<ScenarioResult> Scenarios);

/// <summary>
/// The latencies of one filter scenario, and the item ids each query returned (for recall).
/// </summary>
internal sealed record ScenarioResult(FilterScenario Filter, LatencySummary Latency, IReadOnlyList<long[]> Results);
