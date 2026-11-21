namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// A column of the list view (E14-S02). The names are stored in the column setup, so don't rename them.
/// </summary>
public enum ListColumnKind
{
    /// <summary>
    /// The small thumbnail, always first; it can't be hidden, moved or resized.
    /// </summary>
    Thumbnail,

    Name,
    Type,
    DateTaken,
    Size,
    Resolution,
    Duration,

    /// <summary>
    /// The processing status, with the status badges (and the offline and missing badges of E14-S07).
    /// </summary>
    Status,

    Folder,
    Favorite,
}
