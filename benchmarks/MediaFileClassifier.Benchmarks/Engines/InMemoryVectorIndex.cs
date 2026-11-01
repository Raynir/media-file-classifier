using System.Numerics.Tensors;

namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// How the in-memory index stores vectors.
/// </summary>
internal enum VectorPrecision
{
    Single,
    Half,
}

/// <summary>
/// How the in-memory index scores a vector against the query.
/// </summary>
internal enum ScoreFunction
{
    /// <summary>
    /// <see cref="TensorPrimitives.CosineSimilarity(ReadOnlySpan{float}, ReadOnlySpan{float})"/>.
    /// For half vectors this is the generic <c>CosineSimilarity&lt;Half&gt;</c>.
    /// </summary>
    Cosine,

    /// <summary>
    /// <see cref="TensorPrimitives.Dot(ReadOnlySpan{float}, ReadOnlySpan{float})"/>, which equals
    /// cosine similarity for L2-normalized vectors. Half vectors are widened to float32 first.
    /// </summary>
    Dot,
}

/// <summary>
/// Prototype B: every embedding in one contiguous array, scored brute force with SIMD
/// (System.Numerics.Tensors). An item's vectors are adjacent, so per-item max scoring is a single
/// pass with no per-item state. Upserts append and removals leave a tombstone; compaction is left
/// to the production version (E01-S04).
/// </summary>
internal sealed class InMemoryVectorIndex
{
    private const int MinVectorsPerPart = 8192;

    private readonly int dimensions;
    private readonly VectorPrecision precision;
    private readonly Dictionary<long, int> ordinals = [];
    private float[] singles = [];
    private Half[] halves = [];
    private int[] vectorOrdinals = [];
    private int vectorCount;
    private long[] itemIds = [];
    private int[] itemVectorCounts = [];
    private bool[] removed = [];
    private int itemCount;
    private int removedVectors;

    public InMemoryVectorIndex(int dimensions, VectorPrecision precision, int vectorCapacity = 0)
    {
        this.dimensions = dimensions;
        this.precision = precision;
        EnsureVectorCapacity(vectorCapacity);
    }

    public int Count => ordinals.Count;

    public int VectorCount => vectorCount - removedVectors;

    /// <summary>
    /// The bytes held by the vector array, tombstones included.
    /// </summary>
    public long VectorBytes => (long)vectorCount * dimensions * (precision == VectorPrecision.Single ? sizeof(float) : 2);

    public bool Contains(long itemId) => ordinals.ContainsKey(itemId);

    /// <summary>
    /// Adds an item, or replaces all its vectors. <paramref name="vectors"/> holds one or more
    /// vectors back to back.
    /// </summary>
    public void Upsert(long itemId, ReadOnlySpan<float> vectors)
    {
        var count = Append(itemId, vectors.Length);
        var start = (vectorCount - count) * dimensions;
        if (precision == VectorPrecision.Single)
        {
            vectors.CopyTo(singles.AsSpan(start));
        }
        else
        {
            TensorPrimitives.ConvertToHalf(vectors, halves.AsSpan(start, vectors.Length));
        }
    }

    /// <summary>
    /// Adds an item stored as half precision. Only for <see cref="VectorPrecision.Half"/>.
    /// </summary>
    public void Upsert(long itemId, ReadOnlySpan<Half> vectors)
    {
        if (precision != VectorPrecision.Half)
        {
            throw new InvalidOperationException("The index stores float32 vectors.");
        }

        var count = Append(itemId, vectors.Length);
        vectors.CopyTo(halves.AsSpan((vectorCount - count) * dimensions));
    }

    public bool Remove(long itemId)
    {
        if (!ordinals.Remove(itemId, out var ordinal))
        {
            return false;
        }

        removed[ordinal] = true;
        removedVectors += itemVectorCounts[ordinal];
        return true;
    }

    /// <summary>
    /// Turns the item ids that pass a filter into a mask the search can test in O(1).
    /// </summary>
    public bool[] CreateMask(IEnumerable<long> passingItemIds)
    {
        var mask = new bool[itemCount];
        foreach (var id in passingItemIds)
        {
            if (ordinals.TryGetValue(id, out var ordinal))
            {
                mask[ordinal] = true;
            }
        }

        return mask;
    }

    public IReadOnlyList<SearchHit> Search(
        ReadOnlySpan<float> query,
        int k,
        ScoreFunction function,
        bool[]? mask = null,
        int parallelism = 1)
    {
        if (query.Length != dimensions)
        {
            throw new ArgumentException($"Expected a {dimensions}-dimension query.", nameof(query));
        }

        var singleQuery = query.ToArray();
        var halfQuery = new Half[dimensions];
        TensorPrimitives.ConvertToHalf(query, halfQuery);
        var result = new TopK(k);
        var chunks = SplitByItem(Math.Max(1, parallelism));
        if (chunks.Count == 1)
        {
            ScoreRange(chunks[0].Start, chunks[0].End, singleQuery, halfQuery, function, mask, result);
        }
        else
        {
            var partial = new TopK[chunks.Count];
            Parallel.For(0, chunks.Count, i =>
            {
                partial[i] = new TopK(k);
                ScoreRange(chunks[i].Start, chunks[i].End, singleQuery, halfQuery, function, mask, partial[i]);
            });
            foreach (var topK in partial)
            {
                topK.MergeInto(result);
            }
        }

        return result.ToHits(ordinal => itemIds[ordinal]);
    }

    private int Append(long itemId, int length)
    {
        if (length == 0 || length % dimensions != 0)
        {
            throw new ArgumentException($"Expected one or more {dimensions}-dimension vectors.", nameof(length));
        }

        Remove(itemId);
        var count = length / dimensions;
        EnsureVectorCapacity(vectorCount + count);
        if (itemCount == itemIds.Length)
        {
            var capacity = Math.Max(1024, itemIds.Length * 2);
            Array.Resize(ref itemIds, capacity);
            Array.Resize(ref itemVectorCounts, capacity);
            Array.Resize(ref removed, capacity);
        }

        var ordinal = itemCount++;
        itemIds[ordinal] = itemId;
        itemVectorCounts[ordinal] = count;
        ordinals[itemId] = ordinal;
        vectorOrdinals.AsSpan(vectorCount, count).Fill(ordinal);
        vectorCount += count;
        return count;
    }

    private void EnsureVectorCapacity(int vectors)
    {
        if (vectors <= vectorOrdinals.Length)
        {
            return;
        }

        var capacity = Math.Max(vectors, Math.Max(1024, vectorOrdinals.Length * 2));
        Array.Resize(ref vectorOrdinals, capacity);
        if (precision == VectorPrecision.Single)
        {
            Array.Resize(ref singles, capacity * dimensions);
        }
        else
        {
            Array.Resize(ref halves, capacity * dimensions);
        }
    }

    // Splits the vectors into ranges for parallel scoring, moving each boundary forward so that
    // no item's vectors straddle two ranges.
    private List<(int Start, int End)> SplitByItem(int parallelism)
    {
        // Below a few thousand vectors per part, the fork and join cost more than they save.
        var parts = Math.Clamp((vectorCount + MinVectorsPerPart - 1) / MinVectorsPerPart, 1, parallelism);
        var ranges = new List<(int Start, int End)>(parts);
        var size = Math.Max(1, (vectorCount + parts - 1) / parts);
        var start = 0;
        while (start < vectorCount)
        {
            var end = Math.Min(vectorCount, start + size);
            while (end < vectorCount && vectorOrdinals[end] == vectorOrdinals[end - 1])
            {
                end++;
            }

            ranges.Add((start, end));
            start = end;
        }

        if (ranges.Count == 0)
        {
            ranges.Add((0, 0));
        }

        return ranges;
    }

    private void ScoreRange(
        int start,
        int end,
        float[] singleQuery,
        Half[] halfQuery,
        ScoreFunction function,
        bool[]? mask,
        TopK topK)
    {
        var scratch = precision == VectorPrecision.Half && function == ScoreFunction.Dot ? new float[dimensions] : [];
        var v = start;
        while (v < end)
        {
            var ordinal = vectorOrdinals[v];
            var runEnd = v + itemVectorCounts[ordinal];
            if (!removed[ordinal] && (mask is null || (ordinal < mask.Length && mask[ordinal])))
            {
                var best = float.NegativeInfinity;
                for (var i = v; i < runEnd; i++)
                {
                    best = Math.Max(best, Score(i, singleQuery, halfQuery, function, scratch));
                }

                topK.Offer(ordinal, best);
            }

            v = runEnd;
        }
    }

    private float Score(int vector, float[] singleQuery, Half[] halfQuery, ScoreFunction function, float[] scratch)
    {
        var offset = vector * dimensions;
        if (precision == VectorPrecision.Single)
        {
            var span = new ReadOnlySpan<float>(singles, offset, dimensions);
            return function == ScoreFunction.Dot
                ? TensorPrimitives.Dot(span, singleQuery)
                : TensorPrimitives.CosineSimilarity(span, singleQuery);
        }

        var halfSpan = new ReadOnlySpan<Half>(halves, offset, dimensions);
        if (function == ScoreFunction.Cosine)
        {
            return (float)TensorPrimitives.CosineSimilarity(halfSpan, halfQuery);
        }

        TensorPrimitives.ConvertToSingle(halfSpan, scratch);
        return TensorPrimitives.Dot(scratch, singleQuery);
    }
}
