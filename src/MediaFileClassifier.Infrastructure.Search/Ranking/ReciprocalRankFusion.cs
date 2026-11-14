using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Infrastructure.Search.Ranking;

/// <summary>
/// Merges the keyword and vector result lists with Reciprocal Rank Fusion (E01-S04-T04): an item scores
/// <c>Σ weight / (k + rank)</c> over the lists it's in, with ranks from 1. Only ranks count, so BM25 and cosine
/// scores never need calibrating against each other.
/// </summary>
internal static class ReciprocalRankFusion
{
    /// <summary>
    /// Fuses two ranked lists.
    /// </summary>
    /// <param name="keyword">Document IDs of the keyword list, best first.</param>
    /// <param name="vector">Document IDs of the vector list, best first.</param>
    /// <param name="options">The constant k and the weights.</param>
    /// <returns>Every document of either list, best first. Ties go to the better single rank, then to the keyword
    /// list, then to the lower document ID.</returns>
    public static IReadOnlyList<FusedHit> Fuse(IReadOnlyList<int> keyword, IReadOnlyList<int> vector, SearchRankingOptions options)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var hits = new Dictionary<int, FusedHit>();
        for (var i = 0; i < keyword.Count; i++)
        {
            var rank = i + 1;
            hits.TryAdd(keyword[i], new FusedHit(keyword[i], options.KeywordWeight / (options.RrfK + rank), rank, null));
        }

        for (var i = 0; i < vector.Count; i++)
        {
            var rank = i + 1;
            var score = options.VectorWeight / (options.RrfK + rank);
            hits[vector[i]] = hits.TryGetValue(vector[i], out var hit)
                ? hit.VectorRank is null ? hit with { Score = hit.Score + score, VectorRank = rank } : hit
                : new FusedHit(vector[i], score, null, rank);
        }

        var list = hits.Values.ToList();
        list.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var byBestRank = BestRank(a).CompareTo(BestRank(b));
            if (byBestRank != 0)
            {
                return byBestRank;
            }

            var byKeyword = (a.KeywordRank ?? int.MaxValue).CompareTo(b.KeywordRank ?? int.MaxValue);
            return byKeyword != 0 ? byKeyword : a.DocumentId.CompareTo(b.DocumentId);
        });
        return list;
    }

    private static int BestRank(FusedHit hit) => Math.Min(hit.KeywordRank ?? int.MaxValue, hit.VectorRank ?? int.MaxValue);
}

/// <summary>
/// A fused result.
/// </summary>
/// <param name="DocumentId">The document.</param>
/// <param name="Score">The RRF score.</param>
/// <param name="KeywordRank">Rank in the keyword list, from 1, or <c>null</c>.</param>
/// <param name="VectorRank">Rank in the vector list, from 1, or <c>null</c>.</param>
internal sealed record FusedHit(int DocumentId, double Score, int? KeywordRank, int? VectorRank);
