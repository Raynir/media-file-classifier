namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// The engines the harness can run, by name. Add a new candidate here (for example an ANN library,
/// option C) and it runs through the same harness.
/// </summary>
internal static class EngineCatalog
{
    /// <summary>
    /// The exact float32 baseline that recall is measured against.
    /// </summary>
    public const string Reference = "memory-f32-cosine";

    public static IReadOnlyList<string> Names { get; } =
    [
        Reference,
        "memory-f32-dot",
        "memory-f32-dot-parallel",
        "memory-f16-dot",
        "memory-f16-dot-parallel",
        "memory-f16-cosine-generic",
        "sqlitevec-knn",
        "sqlitevec-knn-tuned",
        "sqlitevec-scan",
        "sqlitevec-scan-tuned",
    ];

    public static IVectorSearchEngine Create(string name, int vectorsPerItem) => name switch
    {
        Reference => new InMemoryEngine(name, VectorPrecision.Single, ScoreFunction.Cosine, 1),
        "memory-f32-dot" => new InMemoryEngine(name, VectorPrecision.Single, ScoreFunction.Dot, 1),
        "memory-f32-dot-parallel" => new InMemoryEngine(name, VectorPrecision.Single, ScoreFunction.Dot, Environment.ProcessorCount),
        "memory-f16-dot" => new InMemoryEngine(name, VectorPrecision.Half, ScoreFunction.Dot, 1),
        "memory-f16-dot-parallel" => new InMemoryEngine(name, VectorPrecision.Half, ScoreFunction.Dot, Environment.ProcessorCount),
        "memory-f16-cosine-generic" => new InMemoryEngine(name, VectorPrecision.Half, ScoreFunction.Cosine, 1),
        "sqlitevec-knn" => new SqliteVecEngine(name, SqliteVecMode.Knn, vectorsPerItem, tuned: false),
        "sqlitevec-knn-tuned" => new SqliteVecEngine(name, SqliteVecMode.Knn, vectorsPerItem, tuned: true),
        "sqlitevec-scan" => new SqliteVecEngine(name, SqliteVecMode.Scan, vectorsPerItem, tuned: false),
        "sqlitevec-scan-tuned" => new SqliteVecEngine(name, SqliteVecMode.Scan, vectorsPerItem, tuned: true),
        _ => throw new ArgumentException($"Unknown engine '{name}'. Known: {string.Join(", ", Names)}.", nameof(name)),
    };
}
