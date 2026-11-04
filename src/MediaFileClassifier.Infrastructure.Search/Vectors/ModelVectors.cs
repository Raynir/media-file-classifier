using System.Buffers.Binary;
using System.Numerics.Tensors;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Search.Vectors;

/// <summary>
/// Every vector of one model in one contiguous half-precision array (D067), scored brute force with
/// <see cref="TensorPrimitives.Dot(ReadOnlySpan{float}, ReadOnlySpan{float})"/> after widening to float32. An item's
/// vectors are adjacent, so per-item best-vector scoring is one pass. Replacing an item appends its new vectors and
/// leaves a tombstone, and the array is compacted once a quarter of it is dead. Not thread-safe; <see cref="VectorIndex"/>
/// guards it.
/// </summary>
/// <param name="dimension">Values per vector.</param>
internal sealed class ModelVectors(int dimension)
{
    /// <summary>
    /// Below this many vectors per part, splitting the scan costs more than it saves (MFC-17).
    /// </summary>
    public const int MinVectorsPerPart = 8192;

    private readonly Dictionary<int, int> ordinalsByDocument = [];
    private readonly Dictionary<Id<MediaItem>, int> ordinalsByItem = [];
    private Half[] halves = [];
    private int[] vectorOrdinals = [];
    private int vectorCount;
    private int[] documentIds = [];
    private Id<MediaItem>[] itemIds = [];
    private int[] firstVectors = [];
    private int[] vectorCounts = [];
    private bool[] removed = [];
    private int itemCount;
    private int removedVectors;

    public int Dimension { get; } = dimension;

    /// <summary>
    /// Gets the number of items with vectors.
    /// </summary>
    public int ItemCount => ordinalsByDocument.Count;

    /// <summary>
    /// Gets the number of live vectors.
    /// </summary>
    public int VectorCount => vectorCount - removedVectors;

    /// <summary>
    /// Adds an item's vectors, or replaces all of them.
    /// </summary>
    /// <param name="documentId">The item's document ID.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="vectors">One or more vectors, back to back, as half-precision little-endian bytes.</param>
    public void Upsert(int documentId, Id<MediaItem> itemId, ReadOnlySpan<byte> vectors)
    {
        var values = vectors.Length / sizeof(short);
        var start = Append(documentId, itemId, values);
        var target = halves.AsSpan(start, values);
        for (var i = 0; i < values; i++)
        {
            target[i] = BinaryPrimitives.ReadHalfLittleEndian(vectors[(i * sizeof(short))..]);
        }

        CompactIfSparse();
    }

    /// <summary>
    /// Adds an item's vectors, or replaces all of them.
    /// </summary>
    /// <param name="documentId">The item's document ID.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="vectors">One or more vectors, back to back.</param>
    public void Upsert(int documentId, Id<MediaItem> itemId, ReadOnlySpan<float> vectors)
    {
        var start = Append(documentId, itemId, vectors.Length);
        TensorPrimitives.ConvertToHalf(vectors, halves.AsSpan(start, vectors.Length));
        CompactIfSparse();
    }

    /// <summary>
    /// Removes an item's vectors.
    /// </summary>
    /// <param name="documentId">The item's document ID.</param>
    /// <returns><c>true</c> if it had vectors.</returns>
    public bool Remove(int documentId)
    {
        if (!ordinalsByDocument.Remove(documentId, out var ordinal))
        {
            return false;
        }

        ordinalsByItem.Remove(itemIds[ordinal]);
        removed[ordinal] = true;
        removedVectors += vectorCounts[ordinal];
        CompactIfSparse();
        return true;
    }

    /// <summary>
    /// Copies an item's first vector: its whole-item vector, or its first keyframe's.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="documentId">The item's document ID.</param>
    /// <returns>The vector, or <c>null</c> if the item has none.</returns>
    public float[]? GetFirstVector(Id<MediaItem> itemId, out int documentId)
    {
        documentId = 0;
        if (!ordinalsByItem.TryGetValue(itemId, out var ordinal))
        {
            return null;
        }

        documentId = documentIds[ordinal];
        var vector = new float[Dimension];
        TensorPrimitives.ConvertToSingle(halves.AsSpan(firstVectors[ordinal] * Dimension, Dimension), vector);
        return vector;
    }

    /// <summary>
    /// Finds the items whose best vector is most similar to the query.
    /// </summary>
    /// <param name="query">The query, L2-normalised, with <see cref="Dimension"/> values.</param>
    /// <param name="k">Number of items to return.</param>
    /// <param name="mask">Which document IDs may be returned, indexed by document ID; <c>null</c> for all.</param>
    /// <param name="excludeDocumentId">A document to leave out, e.g. the item a similarity search starts from; 0 for none.</param>
    /// <param name="parallelism">Most threads to score on.</param>
    /// <returns>The items, most similar first; ties by document ID.</returns>
    public IReadOnlyList<VectorHit> Search(ReadOnlySpan<float> query, int k, bool[]? mask, int excludeDocumentId, int parallelism)
    {
        if (query.Length != Dimension)
        {
            throw new ArgumentException($"The query has {query.Length} values, but the model's vectors have {Dimension}.", nameof(query));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);

        var queryArray = query.ToArray();
        var ranges = SplitByItem(Math.Max(1, parallelism));
        var parts = new TopK[ranges.Count];
        if (ranges.Count == 1)
        {
            parts[0] = ScoreRange(ranges[0].Start, ranges[0].End, queryArray, k, mask, excludeDocumentId);
        }
        else
        {
            Parallel.For(0, ranges.Count, i => parts[i] = ScoreRange(ranges[i].Start, ranges[i].End, queryArray, k, mask, excludeDocumentId));
        }

        var result = new TopK(k);
        foreach (var part in parts)
        {
            part.MergeInto(result);
        }

        return result.ToList(ordinal => (documentIds[ordinal], itemIds[ordinal]));
    }

    private int Append(int documentId, Id<MediaItem> itemId, int values)
    {
        if (values == 0 || values % Dimension != 0)
        {
            throw new ArgumentException($"Expected one or more {Dimension}-value vectors, got {values} values.", nameof(values));
        }

        if (ordinalsByDocument.TryGetValue(documentId, out var old))
        {
            ordinalsByItem.Remove(itemIds[old]);
            ordinalsByDocument.Remove(documentId);
            removed[old] = true;
            removedVectors += vectorCounts[old];
        }

        var count = values / Dimension;
        EnsureVectorCapacity(vectorCount + count);
        if (itemCount == itemIds.Length)
        {
            var capacity = Math.Max(1024, itemIds.Length * 2);
            Array.Resize(ref documentIds, capacity);
            Array.Resize(ref itemIds, capacity);
            Array.Resize(ref firstVectors, capacity);
            Array.Resize(ref vectorCounts, capacity);
            Array.Resize(ref removed, capacity);
        }

        var ordinal = itemCount++;
        documentIds[ordinal] = documentId;
        itemIds[ordinal] = itemId;
        firstVectors[ordinal] = vectorCount;
        vectorCounts[ordinal] = count;
        removed[ordinal] = false;
        ordinalsByDocument[documentId] = ordinal;
        ordinalsByItem[itemId] = ordinal;
        vectorOrdinals.AsSpan(vectorCount, count).Fill(ordinal);
        var start = vectorCount * Dimension;
        vectorCount += count;
        return start;
    }

    private void EnsureVectorCapacity(int vectors)
    {
        if (vectors <= vectorOrdinals.Length)
        {
            return;
        }

        var capacity = Math.Max(vectors, Math.Max(1024, vectorOrdinals.Length * 2));
        Array.Resize(ref vectorOrdinals, capacity);
        Array.Resize(ref halves, capacity * Dimension);
    }

    // Rewrites the arrays without dead items once they're a quarter of the vectors, so replacements during analysis and
    // model migrations don't grow memory without bound.
    private void CompactIfSparse()
    {
        if (removedVectors < 1024 || removedVectors * 4 < vectorCount)
        {
            return;
        }

        var liveVectors = vectorCount - removedVectors;
        var newHalves = new Half[Math.Max(1024, liveVectors) * Dimension];
        var newVectorOrdinals = new int[Math.Max(1024, liveVectors)];
        var liveItems = ordinalsByDocument.Count;
        var newDocumentIds = new int[Math.Max(1024, liveItems)];
        var newItemIds = new Id<MediaItem>[newDocumentIds.Length];
        var newFirstVectors = new int[newDocumentIds.Length];
        var newVectorCounts = new int[newDocumentIds.Length];
        var nextVector = 0;
        var nextItem = 0;
        for (var ordinal = 0; ordinal < itemCount; ordinal++)
        {
            if (removed[ordinal])
            {
                continue;
            }

            var count = vectorCounts[ordinal];
            halves.AsSpan(firstVectors[ordinal] * Dimension, count * Dimension).CopyTo(newHalves.AsSpan(nextVector * Dimension));
            newVectorOrdinals.AsSpan(nextVector, count).Fill(nextItem);
            newDocumentIds[nextItem] = documentIds[ordinal];
            newItemIds[nextItem] = itemIds[ordinal];
            newFirstVectors[nextItem] = nextVector;
            newVectorCounts[nextItem] = count;
            ordinalsByDocument[documentIds[ordinal]] = nextItem;
            ordinalsByItem[itemIds[ordinal]] = nextItem;
            nextVector += count;
            nextItem++;
        }

        halves = newHalves;
        vectorOrdinals = newVectorOrdinals;
        documentIds = newDocumentIds;
        itemIds = newItemIds;
        firstVectors = newFirstVectors;
        vectorCounts = newVectorCounts;
        removed = new bool[newDocumentIds.Length];
        vectorCount = nextVector;
        itemCount = nextItem;
        removedVectors = 0;
    }

    // Splits the vectors into ranges for parallel scoring, moving each boundary forward so that no item's vectors
    // straddle two ranges.
    private List<(int Start, int End)> SplitByItem(int parallelism)
    {
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

    private TopK ScoreRange(int start, int end, float[] query, int k, bool[]? mask, int excludeDocumentId)
    {
        var topK = new TopK(k);
        var scratch = new float[Dimension];
        var v = start;
        while (v < end)
        {
            var ordinal = vectorOrdinals[v];
            var runEnd = v + vectorCounts[ordinal];
            var document = documentIds[ordinal];
            if (!removed[ordinal] && document != excludeDocumentId && (mask is null || (document < mask.Length && mask[document])))
            {
                var best = float.NegativeInfinity;
                for (var i = v; i < runEnd; i++)
                {
                    TensorPrimitives.ConvertToSingle(halves.AsSpan(i * Dimension, Dimension), scratch);
                    best = Math.Max(best, TensorPrimitives.Dot(scratch, query));
                }

                topK.Offer(ordinal, document, best);
            }

            v = runEnd;
        }

        return topK;
    }
}

/// <summary>
/// One vector search result.
/// </summary>
/// <param name="DocumentId">The item's document ID.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Similarity">Cosine similarity of the item's best vector to the query.</param>
internal readonly record struct VectorHit(int DocumentId, Id<MediaItem> ItemId, float Similarity);
