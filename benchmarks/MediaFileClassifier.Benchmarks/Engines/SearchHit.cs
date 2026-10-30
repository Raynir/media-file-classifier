namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// A search result: the item and its cosine similarity to the query (higher is better).
/// </summary>
internal readonly record struct SearchHit(long ItemId, float Similarity);
