namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Keys of the analyzers (E06). They're stored with analysis runs, analyzer states and the analysis profile, so they
/// never change.
/// </summary>
public static class AnalyzerKeys
{
    /// <summary>
    /// The image embedding, used for semantic search and "find similar" (E06-S03). It decides when an item is Indexed (D016).
    /// </summary>
    public const string Embedding = "embedding";

    /// <summary>
    /// Title, description, summary, tags, objects, scenes, actions and emotions (E06-S04).
    /// </summary>
    public const string Describer = "describer";

    /// <summary>
    /// Text visible in the image or its keyframes (E06-S05).
    /// </summary>
    public const string Ocr = "ocr";
}
