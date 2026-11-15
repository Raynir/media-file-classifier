using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Infrastructure.Search.Vectors;

/// <summary>
/// The in-memory vector index of a library (D067): one <see cref="ModelVectors"/> per embedding model. SQLite is the
/// source of truth and this is a cache: it's loaded from the database and then updated after each committed
/// embedding write. Searches run in parallel with each other; updates wait for running searches.
/// </summary>
internal sealed class VectorIndex : IDisposable
{
    private readonly ReaderWriterLockSlim gate = new();
    private readonly Dictionary<Id<ModelRegistration>, ModelVectors> models = [];

    /// <summary>
    /// Gets the number of items the load left out because their stored vectors don't fit their model.
    /// </summary>
    public int SkippedItems { get; private set; }

    /// <summary>
    /// Gets the number of vectors of every model.
    /// </summary>
    public int VectorCount
    {
        get
        {
            gate.EnterReadLock();
            try
            {
                return models.Values.Sum(model => model.VectorCount);
            }
            finally
            {
                gate.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Loads every stored vector.
    /// </summary>
    /// <param name="embeddings">The library's embeddings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The index.</returns>
    public static async Task<VectorIndex> LoadAsync(IEmbeddingRepository embeddings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddings);

        var index = new VectorIndex();
        var loader = new Loader(index);
        await embeddings.ReadAllVectorsAsync(loader.Visit, cancellationToken).ConfigureAwait(false);
        loader.Flush();
        return index;
    }

    /// <summary>
    /// Applies committed embedding writes.
    /// </summary>
    /// <param name="changes">The changes, in commit order.</param>
    public void Apply(IReadOnlyList<CommittedEmbeddings> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        gate.EnterWriteLock();
        try
        {
            foreach (var change in changes)
            {
                var (itemId, modelId, list) = change.Embeddings;
                if (list.Count == 0)
                {
                    if (models.TryGetValue(modelId, out var existing))
                    {
                        existing.Remove(change.DocumentId);
                    }

                    continue;
                }

                var dimension = list[0].Dimension;
                var model = GetOrAddModel(modelId, dimension);
                var values = new float[list.Count * dimension];
                for (var i = 0; i < list.Count; i++)
                {
                    list[i].Vector.Span.CopyTo(values.AsSpan(i * dimension, dimension));
                }

                model.Upsert(change.DocumentId, itemId, values);
            }
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    /// <summary>
    /// Finds the items most similar to a query vector, among its model's vectors only.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="k">Number of items to return.</param>
    /// <param name="mask">Which document IDs may be returned, indexed by document ID; <c>null</c> for all.</param>
    /// <returns>The items, most similar first. Empty if the model has no vectors, or if the item of a "find similar"
    /// query has none.</returns>
    /// <exception cref="ArgumentException">The query's dimension doesn't match the model's vectors.</exception>
    public IReadOnlyList<VectorHit> Search(SearchVector query, int k, bool[]? mask)
    {
        ArgumentNullException.ThrowIfNull(query);

        gate.EnterReadLock();
        try
        {
            if (!models.TryGetValue(query.ModelId, out var model))
            {
                return [];
            }

            var vector = query.Vector;
            var exclude = 0;
            if (query.ItemId is { } itemId)
            {
                if (model.GetFirstVector(itemId, out exclude) is not { } first)
                {
                    return [];
                }

                vector = first;
            }

            return model.Search(vector.Span, k, mask, exclude, Environment.ProcessorCount);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    /// <summary>
    /// Counts the items a search of one model skips (D051): those the mask allows that have vectors of another model
    /// but none of this one, because they were analyzed with an older model (E06-S07-T05).
    /// </summary>
    /// <param name="modelId">The model searched.</param>
    /// <param name="mask">Which document IDs may be returned, indexed by document ID; <c>null</c> for all.</param>
    /// <returns>The number of items.</returns>
    public int CountWithoutModel(Id<ModelRegistration> modelId, bool[]? mask)
    {
        gate.EnterReadLock();
        try
        {
            models.TryGetValue(modelId, out var searched);
            if (models.Count == 0 || (models.Count == 1 && searched is not null))
            {
                return 0;
            }

            // Usually one other model: an item can't be counted twice, so no set is needed.
            var others = models.Where(pair => pair.Key != modelId).Select(pair => pair.Value).ToList();
            var count = 0;
            var skipped = others.Count > 1 ? new HashSet<int>() : null;
            foreach (var other in others)
            {
                foreach (var document in other.DocumentIds)
                {
                    if ((mask is null || (document < mask.Length && mask[document])) && searched?.Contains(document) != true
                        && (skipped is null || skipped.Add(document)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the query's mean similarity over the items of its model (see <see cref="ModelVectors.MeanSimilarity"/>).
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>The mean similarity, or 0 when the model or the "find similar" item has no vectors.</returns>
    public float MeanSimilarity(SearchVector query)
    {
        ArgumentNullException.ThrowIfNull(query);

        gate.EnterReadLock();
        try
        {
            if (!models.TryGetValue(query.ModelId, out var model))
            {
                return 0;
            }

            if (query.ItemId is { } itemId)
            {
                return model.GetFirstVector(itemId, out _) is { } first ? model.MeanSimilarity(first) : 0;
            }

            return model.MeanSimilarity(query.Vector.Span);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    private ModelVectors GetOrAddModel(Id<ModelRegistration> modelId, int dimension)
    {
        if (models.TryGetValue(modelId, out var model))
        {
            if (model.Dimension != dimension)
            {
                throw new InvalidOperationException($"Model {modelId} has {model.Dimension}-value vectors, not {dimension}.");
            }

            return model;
        }

        model = new ModelVectors(dimension);
        models.Add(modelId, model);
        return model;
    }

    /// <summary>
    /// Collects each item's adjacent vectors from the load, and adds them together.
    /// </summary>
    /// <param name="index">The index being loaded.</param>
    private sealed class Loader(VectorIndex index)
    {
        private byte[] buffer = new byte[4096];
        private int length;
        private StoredVector current;
        private bool hasCurrent;

        public void Visit(in StoredVector vector, ReadOnlySpan<byte> halfVector)
        {
            if (hasCurrent && (vector.ModelId != current.ModelId || vector.DocumentId != current.DocumentId))
            {
                Flush();
            }

            if (!hasCurrent)
            {
                current = vector;
                hasCurrent = true;
            }

            if (buffer.Length < length + halfVector.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + halfVector.Length));
            }

            halfVector.CopyTo(buffer.AsSpan(length));
            length += halfVector.Length;
        }

        public void Flush()
        {
            if (!hasCurrent)
            {
                return;
            }

            // Loading happens before the index is shared, so it needs no lock. An item whose rows don't fit its model
            // (a wrong dimension, a truncated vector) is left out rather than failing the whole load.
            try
            {
                index.GetOrAddModel(current.ModelId, current.Dimension).Upsert(current.DocumentId, current.ItemId, buffer.AsSpan(0, length));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                index.SkippedItems++;
            }

            length = 0;
            hasCurrent = false;
        }
    }
}
