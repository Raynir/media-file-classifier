namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// The exact visible text of an image: an entry of <c>ocr-truth.json</c>.
/// Lines are in reading order and separated by <c>\n</c>.
/// </summary>
public sealed record OcrTruthEntry
{
    public required string Id { get; init; }

    /// <summary>
    /// <c>latin</c>, <c>cyrillic</c>, or both.
    /// </summary>
    public required IReadOnlyList<string> Scripts { get; init; }

    /// <summary>
    /// <c>synthetic</c> (rendered from this text) or <c>transcribed</c> (typed from a real photo).
    /// </summary>
    public required string Origin { get; init; }

    public required string Text { get; init; }

    /// <summary>
    /// For transcribed text: whether a second person checked it against the image.
    /// </summary>
    public bool? Verified { get; init; }

    /// <summary>
    /// The script group the CER is reported under: <c>latin</c>, <c>cyrillic</c> or <c>mixed</c>.
    /// </summary>
    public string ScriptGroup => Scripts.Count switch
    {
        1 => Scripts[0],
        _ => "mixed",
    };
}
