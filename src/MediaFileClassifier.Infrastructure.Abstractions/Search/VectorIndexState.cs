namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// Whether the in-memory vector index (D067) can answer queries.
/// </summary>
public enum VectorIndexState
{
    /// <summary>
    /// No library is open.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The vectors are loading. Queries with a vector use the keyword part only, and say so in
    /// <see cref="SearchResults.VectorSearchSkipped"/>.
    /// </summary>
    Loading,

    /// <summary>
    /// Vector search is available.
    /// </summary>
    Ready,

    /// <summary>
    /// Loading failed; it's logged. Keyword search still works, and a rebuild tries again.
    /// </summary>
    Failed,
}
