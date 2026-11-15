using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the items' embeddings (D067): one row per vector, half precision. It's the only write path for embeddings,
/// because the in-memory vector index learns about changes from <see cref="Changed"/>, which it raises after each
/// commit. Writing embeddings any other way leaves the index stale until the next rebuild.
/// </summary>
public interface IEmbeddingRepository
{
    /// <summary>
    /// Raised after embeddings were written and committed, once per write or batch, on the library's writer and in
    /// commit order, before the next write starts. The arguments carry the new vectors, so a listener needn't read them
    /// back. A listener must be quick and must not write to the library.
    /// </summary>
    event EventHandler<EmbeddingsChangedEventArgs>? Changed;

    /// <summary>
    /// Replaces every embedding of an item from one model, e.g. after analysis (E06) or a model migration (E06-S07).
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="modelId">The model.</param>
    /// <param name="embeddings">The new embeddings, all of the item and the model; empty removes them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the embeddings are committed and <see cref="Changed"/> was raised.</returns>
    /// <exception cref="ArgumentException">An embedding belongs to another item or model.</exception>
    /// <exception cref="KeyNotFoundException">The item doesn't exist.</exception>
    Task ReplaceAsync(Id<MediaItem> itemId, Id<ModelRegistration> modelId, IReadOnlyList<Embedding> embeddings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every embedding of an item from one model, as the other overload does, and when
    /// <paramref name="removeOtherModels"/> is set also removes the item's embeddings of every other model in the same
    /// write: once the model is the active search model, the item's older vectors are only kept until then (E06-S07-T05).
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="modelId">The model.</param>
    /// <param name="embeddings">The new embeddings, all of the item and the model; empty removes them.</param>
    /// <param name="removeOtherModels">Whether to remove the item's embeddings of other models.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the embeddings are committed and <see cref="Changed"/> was raised.</returns>
    /// <exception cref="ArgumentException">An embedding belongs to another item or model.</exception>
    /// <exception cref="KeyNotFoundException">The item doesn't exist.</exception>
    Task ReplaceAsync(
        Id<MediaItem> itemId,
        Id<ModelRegistration> modelId,
        IReadOnlyList<Embedding> embeddings,
        bool removeOtherModels,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the models that have stored vectors.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The models, in no particular order.</returns>
    Task<IReadOnlyList<Id<ModelRegistration>>> GetModelsInUseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures a model migration (E06-S07-T05): how many items that aren't Missing have vectors of the old model, and
    /// how many of those also have vectors of one of the new models.
    /// </summary>
    /// <param name="oldModelId">The model search uses.</param>
    /// <param name="newModelIds">The registrations of the model analysis uses now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counts.</returns>
    Task<EmbeddingMigrationProgress> GetMigrationProgressAsync(
        Id<ModelRegistration> oldModelId,
        IReadOnlyCollection<Id<ModelRegistration>> newModelIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the vectors of every other model from the items that have vectors of one of the kept models, after
    /// search switched to them (E06-S07-T05). Items without such vectors keep theirs. It runs in batches of 100 items,
    /// each its own short write that raises <see cref="Changed"/> with an empty change per removed item and model, so
    /// the writer is never held for long (D072).
    /// </summary>
    /// <param name="keptModelIds">The registrations of the model search uses now.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>The number of items that lost vectors.</returns>
    Task<int> RemoveSupersededAsync(IReadOnlyCollection<Id<ModelRegistration>> keptModelIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the embeddings of many items, as <see cref="ReplaceAsync(Id{MediaItem}, Id{ModelRegistration}, IReadOnlyList{Embedding}, CancellationToken)"/> does, in batches of 100 items, each its own
    /// short write (D072). Items that no longer exist are skipped; when an item and model appear more than once, the
    /// last entry wins.
    /// </summary>
    /// <param name="changes">The items, models and new embeddings.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>A task that completes when every batch is committed.</returns>
    Task ReplaceRangeAsync(IReadOnlyList<ItemEmbeddings> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an item's embeddings of every model.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embeddings.</returns>
    Task<IReadOnlyList<Embedding>> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every stored vector, for the in-memory index to load, in one consistent snapshot. Vectors come grouped by
    /// model and then by item, each item's whole-item vector first and then its keyframes in order. Vectors of
    /// Excluded items are read too; search filters them out.
    /// </summary>
    /// <param name="visitor">Called for each vector, on the reading thread. The span is only valid during the call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every vector was read.</returns>
    Task ReadAllVectorsAsync(StoredVectorVisitor visitor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Receives one stored vector.
/// </summary>
/// <param name="vector">Where the vector belongs.</param>
/// <param name="halfVector">The vector as a little-endian half-precision BLOB, L2-normalised (D067).</param>
public delegate void StoredVectorVisitor(in StoredVector vector, ReadOnlySpan<byte> halfVector);

/// <summary>
/// Where a stored vector belongs.
/// </summary>
/// <param name="ModelId">The model.</param>
/// <param name="DocumentId">The item's search document ID.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Dimension">Number of values.</param>
public readonly record struct StoredVector(Id<ModelRegistration> ModelId, int DocumentId, Id<MediaItem> ItemId, int Dimension);

/// <summary>
/// An item's new embeddings of one model.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="ModelId">The model.</param>
/// <param name="Embeddings">All the item's embeddings of the model; empty removes them. Whole-item vector first, then
/// keyframes in order.</param>
public sealed record ItemEmbeddings(Id<MediaItem> ItemId, Id<ModelRegistration> ModelId, IReadOnlyList<Embedding> Embeddings);

/// <summary>
/// How far a model migration got (E06-S07-T05).
/// </summary>
/// <param name="ItemsWithOldModel">Items that aren't Missing and have vectors of the old model.</param>
/// <param name="ItemsWithBoth">Those of them that also have vectors of the new model.</param>
public readonly record struct EmbeddingMigrationProgress(int ItemsWithOldModel, int ItemsWithBoth)
{
    /// <summary>
    /// Gets the share of the items with old vectors that have new ones too; 1 when no item has old vectors.
    /// </summary>
    public double Share => ItemsWithOldModel == 0 ? 1 : (double)ItemsWithBoth / ItemsWithOldModel;
}

/// <summary>
/// Embeddings that were committed.
/// </summary>
/// <param name="changes">Each changed item and model, with its document ID.</param>
public sealed class EmbeddingsChangedEventArgs(IReadOnlyList<CommittedEmbeddings> changes) : EventArgs
{
    /// <summary>
    /// Gets the changes, in the order they were written.
    /// </summary>
    public IReadOnlyList<CommittedEmbeddings> Changes { get; } = changes;
}

/// <summary>
/// An item's committed embeddings of one model.
/// </summary>
/// <param name="DocumentId">The item's search document ID.</param>
/// <param name="Embeddings">The item's new embeddings of the model; empty when they were removed.</param>
public sealed record CommittedEmbeddings(int DocumentId, ItemEmbeddings Embeddings);
