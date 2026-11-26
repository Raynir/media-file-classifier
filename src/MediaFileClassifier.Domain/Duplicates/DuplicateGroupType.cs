namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// How the items of a <see cref="DuplicateGroup"/> were found to be duplicates (E12). Stored as an integer.
/// </summary>
public enum DuplicateGroupType
{
    /// <summary>
    /// The items share a full content hash: byte-identical copies (E12-S01).
    /// </summary>
    Exact = 0,
}
