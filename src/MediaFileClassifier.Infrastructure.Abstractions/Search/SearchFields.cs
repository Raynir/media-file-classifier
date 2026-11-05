namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// The fields of the keyword index, over the items' effective metadata values (spec 1.5).
/// </summary>
[Flags]
public enum SearchFields
{
    None = 0,

    Title = 1 << 0,

    /// <summary>
    /// The description and the summary.
    /// </summary>
    Description = 1 << 1,

    /// <summary>
    /// The effective tags.
    /// </summary>
    Tags = 1 << 2,

    OcrText = 1 << 3,

    Transcript = 1 << 4,

    /// <summary>
    /// The notes and the list fields: objects, scenes, actions, emotions and people.
    /// </summary>
    Other = 1 << 5,

    /// <summary>
    /// The item's path within its folder: matched by word prefix, or by substring for words of three or more characters.
    /// </summary>
    FileName = 1 << 6,
}
