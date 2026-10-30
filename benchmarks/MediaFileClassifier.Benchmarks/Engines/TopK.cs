namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// Keeps the k best-scoring entries seen so far in a min-heap.
/// </summary>
internal sealed class TopK(int k)
{
    private readonly PriorityQueue<int, float> heap = new(k + 1);

    public void Offer(int entry, float score)
    {
        if (heap.Count < k)
        {
            heap.Enqueue(entry, score);
        }
        else if (heap.TryPeek(out _, out var worst) && score > worst)
        {
            heap.DequeueEnqueue(entry, score);
        }
    }

    public void MergeInto(TopK other)
    {
        foreach (var (entry, score) in heap.UnorderedItems)
        {
            other.Offer(entry, score);
        }
    }

    /// <summary>
    /// Returns the entries best first, mapping each entry to a hit.
    /// </summary>
    public List<SearchHit> ToHits(Func<int, long> itemId)
    {
        var hits = new List<SearchHit>(heap.Count);
        foreach (var (entry, score) in heap.UnorderedItems)
        {
            hits.Add(new SearchHit(itemId(entry), score));
        }

        hits.Sort(static (a, b) => b.Similarity.CompareTo(a.Similarity));
        return hits;
    }
}
