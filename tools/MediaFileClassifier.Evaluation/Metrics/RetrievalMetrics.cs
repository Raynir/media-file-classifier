namespace MediaFileClassifier.Evaluation.Metrics;

/// <summary>
/// Search quality per query. Relevance is binary, and a ranked list may repeat an item:
/// only its first occurrence counts.
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>
    /// Capped recall: the relevant items found in the first <paramref name="k"/> results, divided by
    /// the number of relevant items or <paramref name="k"/>, whichever is smaller. A perfect ranking
    /// scores 1 even when a query has more than <paramref name="k"/> relevant items (as in BEIR).
    /// </summary>
    public static double RecallAt(int k, IReadOnlyList<string> ranked, IReadOnlySet<string> relevant)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        if (relevant.Count == 0)
        {
            throw new ArgumentException("A query needs at least one relevant item.", nameof(relevant));
        }

        var found = Distinct(ranked).Take(k).Count(relevant.Contains);
        return (double)found / Math.Min(relevant.Count, k);
    }

    /// <summary>
    /// The 1-based rank of the first relevant result, or null when none is returned.
    /// </summary>
    public static int? FirstRelevantRank(IReadOnlyList<string> ranked, IReadOnlySet<string> relevant)
    {
        var rank = 0;
        foreach (var id in Distinct(ranked))
        {
            rank++;
            if (relevant.Contains(id))
            {
                return rank;
            }
        }

        return null;
    }

    /// <summary>
    /// One over the rank of the first relevant result, or 0. The mean over queries is the MRR.
    /// </summary>
    public static double ReciprocalRank(IReadOnlyList<string> ranked, IReadOnlySet<string> relevant) =>
        FirstRelevantRank(ranked, relevant) is { } rank ? 1.0 / rank : 0.0;

    private static IEnumerable<string> Distinct(IReadOnlyList<string> ranked)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return ranked.Where(seen.Add);
    }
}
