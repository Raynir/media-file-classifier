using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// A search system under evaluation. The runner indexes the data set once, then asks each query.
/// Implementations: <see cref="ResultsFile"/> for results computed elsewhere (e.g. by the model spike's
/// Python scripts), and later the app's own search pipeline in-process (E06-S03-T05).
/// </summary>
public interface IRetrievalSystem
{
    string Name { get; }

    Task IndexAsync(IReadOnlyList<CorpusItem> items, string mediaRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Returns item IDs, best first. The runner asks for <paramref name="limit"/> results.
    /// </summary>
    Task<IReadOnlyList<string>> SearchAsync(EvalQuery query, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// An OCR engine under evaluation.
/// </summary>
public interface IOcrEngine
{
    string Name { get; }

    /// <summary>
    /// Returns the recognized text in reading order, or null when the engine has no output for the item.
    /// </summary>
    Task<string?> RecognizeAsync(CorpusItem item, string path, CancellationToken cancellationToken);
}
