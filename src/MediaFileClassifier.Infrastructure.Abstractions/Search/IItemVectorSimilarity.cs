using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// Compares items by their stored embeddings in the in-memory vector index (D067), for visual near-duplicate
/// detection (E12-S02). It compares each item's first vector: a still's only vector, or a video's whole-item mean.
/// Both calls return <c>null</c> while the index isn't <see cref="VectorIndexState.Ready"/> for
/// the caller's library; <see cref="ISearchIndex.VectorStateChanged"/> tells when that changes.
/// </summary>
public interface IItemVectorSimilarity
{
    /// <summary>
    /// Gets the state of a library's vector index.
    /// </summary>
    /// <param name="library">The library the caller works on.</param>
    /// <returns><see cref="VectorIndexState.Ready"/> when the other calls answer for it;
    /// <see cref="VectorIndexState.Loading"/> while its vectors load, or the index isn't attached to the library yet;
    /// <see cref="VectorIndexState.Failed"/> when loading failed, until a rebuild or the library is opened again.</returns>
    VectorIndexState GetState(IOpenLibrary library);

    /// <summary>
    /// Finds the items most similar to an item.
    /// </summary>
    /// <param name="library">The library the caller works on.</param>
    /// <param name="modelId">The embedding model whose vectors are compared.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="count">Most items to return.</param>
    /// <param name="minSimilarity">Smallest cosine similarity to return.</param>
    /// <returns>The items, most similar first, without <paramref name="itemId"/>; empty if it has no vector of the
    /// model; <c>null</c> if the index isn't ready.</returns>
    IReadOnlyList<ItemSimilarity>? FindNearest(IOpenLibrary library, Id<ModelRegistration> modelId, Id<MediaItem> itemId, int count, float minSimilarity);

    /// <summary>
    /// Gets the cosine similarity of two items.
    /// </summary>
    /// <param name="library">The library the caller works on.</param>
    /// <param name="modelId">The embedding model whose vectors are compared.</param>
    /// <param name="first">One item.</param>
    /// <param name="second">The other item.</param>
    /// <returns>The similarity, from -1 to 1; <see cref="float.NaN"/> when either item has no vector of the model;
    /// <c>null</c> if the index isn't ready.</returns>
    float? GetSimilarity(IOpenLibrary library, Id<ModelRegistration> modelId, Id<MediaItem> first, Id<MediaItem> second);
}

/// <summary>
/// An item found by <see cref="IItemVectorSimilarity.FindNearest"/>.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Similarity">Its cosine similarity to the query item.</param>
public readonly record struct ItemSimilarity(Id<MediaItem> ItemId, float Similarity);
