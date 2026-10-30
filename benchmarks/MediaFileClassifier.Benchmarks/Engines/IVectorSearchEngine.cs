using MediaFileClassifier.Benchmarks.Data;

namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// One way of answering "the k items most similar to this query embedding", optionally filtered.
/// Results are per item: an item with several embeddings scores as its best one.
/// </summary>
internal interface IVectorSearchEngine : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Opens the library and prepares for queries. The harness times this as the load time.
    /// </summary>
    void Load(string databasePath, int dimensions);

    /// <summary>
    /// Returns up to <paramref name="k"/> items, best first.
    /// </summary>
    IReadOnlyList<SearchHit> Search(ReadOnlySpan<float> query, FilterScenario filter, int k);
}
