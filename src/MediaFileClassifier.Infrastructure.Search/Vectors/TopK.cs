using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Search.Vectors;

/// <summary>
/// Keeps the k best entries seen so far in a min-heap: highest score first, and on equal scores the lower document ID,
/// so results don't depend on how the scan was split.
/// </summary>
/// <param name="k">Number of entries to keep.</param>
internal sealed class TopK(int k)
{
    private readonly PriorityQueue<int, (float Score, int Document)> heap = new(k + 1, WorstFirst.Instance);

    /// <summary>
    /// Offers an entry.
    /// </summary>
    /// <param name="ordinal">The entry.</param>
    /// <param name="document">Its document ID, the tie-breaker.</param>
    /// <param name="score">Its score.</param>
    public void Offer(int ordinal, int document, float score)
    {
        if (heap.Count < k)
        {
            heap.Enqueue(ordinal, (score, document));
        }
        else if (heap.TryPeek(out _, out var worst) && WorstFirst.Instance.Compare((score, document), worst) > 0)
        {
            heap.DequeueEnqueue(ordinal, (score, document));
        }
    }

    /// <summary>
    /// Offers every entry of this one to another.
    /// </summary>
    /// <param name="other">The other.</param>
    public void MergeInto(TopK other)
    {
        foreach (var (ordinal, (score, document)) in heap.UnorderedItems)
        {
            other.Offer(ordinal, document, score);
        }
    }

    /// <summary>
    /// Lists the entries best first.
    /// </summary>
    /// <param name="identify">Maps an entry to its document and item.</param>
    /// <returns>The hits.</returns>
    public List<VectorHit> ToList(Func<int, (int Document, Id<MediaItem> Item)> identify)
    {
        var hits = new List<VectorHit>(heap.Count);
        foreach (var (ordinal, (score, _)) in heap.UnorderedItems)
        {
            var (document, item) = identify(ordinal);
            hits.Add(new VectorHit(document, item, score));
        }

        hits.Sort(static (a, b) => b.Similarity != a.Similarity ? b.Similarity.CompareTo(a.Similarity) : a.DocumentId.CompareTo(b.DocumentId));
        return hits;
    }

    /// <summary>
    /// Orders worse entries first: lower score, then higher document ID.
    /// </summary>
    private sealed class WorstFirst : IComparer<(float Score, int Document)>
    {
        public static readonly WorstFirst Instance = new();

        public int Compare((float Score, int Document) x, (float Score, int Document) y) =>
            x.Score != y.Score ? x.Score.CompareTo(y.Score) : y.Document.CompareTo(x.Document);
    }
}
