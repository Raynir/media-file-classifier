using System.Diagnostics;
using System.Text.Json;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Engines;
using MediaFileClassifier.Benchmarks.Measurement;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// Measures one engine on one library. It runs in a process of its own, so that load time and
/// memory are not skewed by an engine that ran before it.
/// </summary>
internal static class Worker
{
    // Warm-up runs cycle over the first queries, which are not measured.
    private const int WarmupQueries = 3;
    private const int MaxWarmupQueries = 60;
    private static readonly TimeSpan WarmupTime = TimeSpan.FromSeconds(2);

    public static int Run(WorkerOptions options)
    {
        using var engine = EngineCatalog.Create(options.Engine, options.VectorsPerItem);
        float[] queries;
        int queryCount;
        using (var connection = SqliteDatabase.Open(options.DatabasePath, loadVec: false, readOnly: true))
        {
            queries = SqliteDatabase.ReadQueries(connection, options.Queries + WarmupQueries, options.Dimensions, out queryCount);
        }

        var baseline = MemorySnapshot.Take();
        var stopwatch = Stopwatch.StartNew();
        engine.Load(options.DatabasePath, options.Dimensions);
        var loadMs = stopwatch.Elapsed.TotalMilliseconds;
        var afterLoad = MemorySnapshot.Take();

        // The first query after load includes JIT, statement preparation and, for SQLite, reading
        // the vector pages: what the user waits for on the first search after startup.
        stopwatch.Restart();
        engine.Search(Query(queries, options.Dimensions, 0), FilterScenario.None, options.K);
        var firstQueryMs = stopwatch.Elapsed.TotalMilliseconds;

        // Warm up until tiered JIT has optimized the hot loops, so the numbers are steady state.
        foreach (var filter in Enum.GetValues<FilterScenario>())
        {
            var warmup = Stopwatch.StartNew();
            for (var i = 0; i < WarmupQueries || (i < MaxWarmupQueries && warmup.Elapsed < WarmupTime); i++)
            {
                engine.Search(Query(queries, options.Dimensions, i % WarmupQueries), filter, options.K);
            }
        }

        var scenarios = new List<ScenarioResult>();
        foreach (var filter in Enum.GetValues<FilterScenario>())
        {
            var latencies = new List<double>();
            var results = new List<long[]>();
            var budget = Stopwatch.StartNew();
            for (var q = WarmupQueries; q < queryCount; q++)
            {
                stopwatch.Restart();
                var hits = engine.Search(Query(queries, options.Dimensions, q), filter, options.K);
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                results.Add([.. hits.Select(hit => hit.ItemId)]);
                if (budget.Elapsed > options.Budget && latencies.Count >= 5)
                {
                    break;
                }
            }

            var summary = LatencySummary.From(latencies);
            scenarios.Add(new ScenarioResult(filter, summary, results));
            Console.WriteLine($"    {options.Engine,-26} {filter,-7} p50 {summary.P50,8:F2} ms  p95 {summary.P95,8:F2} ms  ({summary.Count} queries)");
        }

        var result = new WorkerResult(
            options.Engine,
            options.Library,
            loadMs,
            firstQueryMs,
            baseline,
            afterLoad,
            MemorySnapshot.Take(),
            scenarios);
        File.WriteAllText(options.ResultPath, JsonSerializer.Serialize(result, BenchmarkJson.Options));
        return 0;
    }

    private static ReadOnlySpan<float> Query(float[] queries, int dimensions, int index) =>
        queries.AsSpan(index * dimensions, dimensions);
}
