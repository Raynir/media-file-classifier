namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Descriptive metadata fields (spec 1.4). Each value records its source; see <see cref="DescriptiveFields"/> for the field kinds.
/// </summary>
public enum DescriptiveField
{
    Title,
    Description,
    Summary,

    /// <summary>
    /// Free notes. User-only: neither the file nor AI ever provides them.
    /// </summary>
    Notes,

    OcrText,

    /// <summary>
    /// Speech transcript of a video.
    /// </summary>
    Transcript,

    /// <summary>
    /// Recognized objects. A list field.
    /// </summary>
    Objects,

    /// <summary>
    /// Scenes or settings. A list field.
    /// </summary>
    Scenes,

    /// <summary>
    /// Actions shown. A list field.
    /// </summary>
    Actions,

    /// <summary>
    /// Emotions shown. A list field.
    /// </summary>
    Emotions,

    /// <summary>
    /// People or characters. A list field.
    /// </summary>
    People,
}
