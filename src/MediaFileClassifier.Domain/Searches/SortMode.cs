namespace MediaFileClassifier.Domain.Searches;

/// <summary>
/// Order of search results (spec 1.7).
/// </summary>
public enum SortMode
{
    Relevance,
    Name,

    /// <summary>
    /// Date taken, falling back to the file modification date.
    /// </summary>
    Date,

    Size,

    /// <summary>
    /// When the item was added to the library.
    /// </summary>
    ImportDate,
}
