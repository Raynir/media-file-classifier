namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// A rule that ranks the members of a duplicate group when suggesting which one to keep (E12-S03-T03). The user
/// orders the rules; a rule only decides between members the earlier rules couldn't tell apart.
/// </summary>
public enum KeeperRule
{
    /// <summary>
    /// The member with the most user metadata: tags, edited fields and the favorite mark.
    /// </summary>
    MostUserMetadata,

    /// <summary>
    /// The member with the most pixels.
    /// </summary>
    HighestResolution,

    /// <summary>
    /// The largest file, i.e. the least compressed.
    /// </summary>
    LargestFile,

    /// <summary>
    /// The member in the more original format, over a converted one (see <see cref="KeeperRules.FormatRank"/>).
    /// </summary>
    OriginalFormat,

    /// <summary>
    /// The member whose file was created first.
    /// </summary>
    Oldest,
}
