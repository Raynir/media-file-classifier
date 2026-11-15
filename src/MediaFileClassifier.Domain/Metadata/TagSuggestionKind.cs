namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// What a new AI run suggests about a tag (D020, E06-S07-T04).
/// </summary>
public enum TagSuggestionKind
{
    /// <summary>
    /// The new run gives a tag its analyzer's previous run didn't, and the user neither owns nor rejected it.
    /// </summary>
    Added,

    /// <summary>
    /// The user accepted the tag from the analyzer, and the analyzer's new run no longer gives it.
    /// </summary>
    Removed,
}
