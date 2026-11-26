namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// The review state of a <see cref="DuplicateGroup"/> (E12-S01-T01). Only <see cref="Open"/> groups are suggested to the
/// user. Stored as an integer.
/// </summary>
public enum DuplicateGroupState
{
    /// <summary>
    /// Waiting for review.
    /// </summary>
    Open = 0,

    /// <summary>
    /// The user resolved the group, e.g. kept one copy.
    /// </summary>
    Resolved = 1,

    /// <summary>
    /// The user said the items aren't a problem. The group isn't suggested again unless a new member joins.
    /// </summary>
    Ignored = 2,
}
