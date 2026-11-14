namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// Finds the items of the open library (E01-S04): by keyword (FTS5), by vector similarity, or both combined, always
/// narrowed by structured filters. It's the engine under the search UI (E09) and the filter panel (E10).
/// </summary>
/// <remarks>
/// <para>How it covers the search methods of spec 1.6:</para>
/// <list type="bullet">
/// <item><b>Keyword, tag, OCR text, transcript and filename search:</b> <see cref="SearchQuery.Text"/>. Every word must
/// match a field (title, description, tags, OCR text, transcript, the other descriptive fields or the file path) as a
/// word prefix, or the file path as a substring; case and diacritics are ignored (D029).</item>
/// <item><b>Natural language search:</b> <see cref="SearchQuery.Vector"/> from the query text encoded by the embedding
/// model's text tower (E06-S03), usually together with <see cref="SearchQuery.Text"/> as a hybrid query.</item>
/// <item><b>Visual similarity and image-to-image search:</b> <see cref="SearchVector.FromItem"/> for an indexed item,
/// or <see cref="SearchQuery.Vector"/> from a reference image encoded by the image tower.</item>
/// <item><b>Combined with structured filtering:</b> <see cref="SearchQuery.Filter"/> applies to every mode.</item>
/// </list>
/// <para>
/// A metadata change is searchable as soon as its write commits (spec 1.5, 3.7): the keyword index is written in the
/// same transaction, and the vector index is updated right after the commit (D067).
/// </para>
/// <para>With no library open, a search returns no results and a rebuild does nothing.</para>
/// </remarks>
public interface ISearchIndex
{
    /// <summary>
    /// Raised after <see cref="VectorState"/> changed, e.g. when the vectors finished loading after the library opened.
    /// It may be raised on any thread.
    /// </summary>
    event EventHandler? VectorStateChanged;

    /// <summary>
    /// Gets whether vector search is available yet. Keyword search and filtering work at once; the vectors load in the
    /// background after the library opens (D067).
    /// </summary>
    VectorIndexState VectorState { get; }

    /// <summary>
    /// Runs a search.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of results.</returns>
    /// <exception cref="ArgumentException">The query vector's dimension doesn't match the model's vectors.</exception>
    Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the keyword and vector indexes from the source tables (spec 1.2, "Rebuild or refresh the index").
    /// The keyword index is rebuilt in short batches, so other writes get their turn (D072), and searches keep working.
    /// </summary>
    /// <param name="progress">Receives the share of items done, from 0 to 1.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>A task that completes when both indexes are rebuilt.</returns>
    Task RebuildAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
