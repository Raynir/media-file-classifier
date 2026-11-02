namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// The result of matching a file against a library: the first tier that matched, and every item it matched. Several
/// items match when the library holds duplicates; an exact match applies to each of them (D076).
/// </summary>
/// <param name="Type">The tier that matched.</param>
/// <param name="Confidence">How sure the match is.</param>
/// <param name="Items">The matched items, best first. Empty for no match.</param>
public sealed record FileMatch(FileMatchType Type, MatchConfidence Confidence, IReadOnlyList<MatchedItem> Items)
{
    /// <summary>
    /// Gets the result for a file that matches nothing.
    /// </summary>
    public static FileMatch None { get; } = new(FileMatchType.None, MatchConfidence.None, []);
}
