using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.UseCases.Metadata.EditMetadata;

/// <summary>
/// The size limits of the values a user can write (E07-S01-T04). They keep the details panel, the keyword index and the
/// library file bounded; AI values and values read from files are not limited here.
/// </summary>
public static class MetadataLimits
{
    /// <summary>
    /// The most characters of a title.
    /// </summary>
    public const int TitleLength = 200;

    /// <summary>
    /// The most characters of a description, a summary or the notes.
    /// </summary>
    public const int DescriptionLength = 10_000;

    /// <summary>
    /// The most characters of the OCR text or a transcript.
    /// </summary>
    public const int LongTextLength = 100_000;

    /// <summary>
    /// The most entries of a list field.
    /// </summary>
    public const int ListItems = 100;

    /// <summary>
    /// The most characters of one list entry.
    /// </summary>
    public const int ListItemLength = 100;

    /// <summary>
    /// The most characters of a tag.
    /// </summary>
    public const int TagLength = 100;

    /// <summary>
    /// The most tags the user gives one item.
    /// </summary>
    public const int UserTagsPerItem = 200;

    /// <summary>
    /// Gets the most characters of a text field.
    /// </summary>
    /// <param name="field">The field, which must be a text field.</param>
    /// <returns>The limit.</returns>
    public static int TextLength(DescriptiveField field) => field switch
    {
        DescriptiveField.Title => TitleLength,
        DescriptiveField.OcrText or DescriptiveField.Transcript => LongTextLength,
        _ => DescriptionLength,
    };
}
