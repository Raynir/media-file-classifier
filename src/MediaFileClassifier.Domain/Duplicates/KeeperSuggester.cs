namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// Suggests which member of a duplicate group to keep (E12-S03-T03). The rules are applied in order, each one keeping
/// only the members that rank best by it; the first rule that leaves a single member decides.
/// </summary>
public static class KeeperSuggester
{
    /// <summary>
    /// Suggests the keeper.
    /// </summary>
    /// <param name="members">The group's members.</param>
    /// <param name="order">The rules in order; <see langword="null"/> for <see cref="KeeperRules.Default"/>. It's
    /// made complete as <see cref="KeeperRules.Normalize"/> does.</param>
    /// <returns>The suggestion, or <see langword="null"/> when no member can be kept.</returns>
    public static KeeperSuggestion? Suggest(IReadOnlyCollection<KeeperCandidate> members, IEnumerable<KeeperRule>? order = null)
    {
        ArgumentNullException.ThrowIfNull(members);

        List<KeeperCandidate> remaining = [.. members.Where(member => member.IsAvailable)];
        if (remaining.Count == 0)
        {
            return null;
        }

        if (remaining.Count > 1)
        {
            foreach (var rule in KeeperRules.Normalize(order))
            {
                var best = remaining.Max(member => Score(rule, member));
                remaining = [.. remaining.Where(member => Score(rule, member) == best)];
                if (remaining.Count == 1)
                {
                    return new KeeperSuggestion(remaining[0].ItemId, rule);
                }
            }
        }

        // Every rule tied, or only one member can be kept: no rule decided. The first of the tied members is taken,
        // so the choice follows the order of the members.
        return new KeeperSuggestion(remaining[0].ItemId, null);
    }

    // Higher is better.
    private static long Score(KeeperRule rule, KeeperCandidate member) => rule switch
    {
        KeeperRule.MostUserMetadata => member.UserMetadataCount,
        KeeperRule.HighestResolution => member.Pixels,
        KeeperRule.LargestFile => member.SizeBytes,
        KeeperRule.OriginalFormat => -KeeperRules.FormatRank(member.Format),
        KeeperRule.Oldest => -member.FileCreatedAt.UtcTicks,
        _ => 0,
    };
}
