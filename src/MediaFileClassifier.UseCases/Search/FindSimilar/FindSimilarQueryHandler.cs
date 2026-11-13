using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.FindSimilar;

/// <summary>
/// Runs <see cref="FindSimilarQuery"/>: a vector search from the item's own embedding
/// (<see cref="SearchVector.FromItem"/>), so nothing is encoded. An item without an embedding comes back as
/// <see cref="SemanticSearchStatus.NotAnalyzed"/>, for "Analyze now" (D036).
/// </summary>
/// <param name="index">The search index.</param>
/// <param name="session">The open library, to read the item's embeddings.</param>
/// <param name="filters">The registered filters, for the request's <see cref="FindSimilarQuery.Filters"/>.</param>
public sealed class FindSimilarQueryHandler(ISearchIndex index, ILibrarySession session, FilterRegistry filters) : IRequestHandler<FindSimilarQuery, MediaSearchResults>
{
    /// <inheritdoc />
    public async Task<MediaSearchResults> Handle(FindSimilarQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return Empty(SemanticSearchStatus.Unavailable);
        }

        var embeddings = await library.Embeddings.GetAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
        if (PickModel(embeddings) is not { } modelId)
        {
            return Empty(SemanticSearchStatus.NotAnalyzed);
        }

        var results = await index.SearchAsync(
            new SearchQuery
            {
                Vector = SearchVector.FromItem(modelId, request.ItemId),
                Filter = request.Filters.ApplyTo(request.Filter, filters),
                Sort = request.Sort,
                ReverseSort = request.ReverseSort,
                Offset = request.Offset,
                Limit = request.Limit,
                IncludeMatchedFields = false,
            },
            cancellationToken).ConfigureAwait(false);
        var status = results.VectorSearchSkipped ? SemanticSearchStatus.IndexNotReady : SemanticSearchStatus.Used;
        return new MediaSearchResults(results.Hits, results.TotalCount, [], SearchMode.Similar, status);
    }

    // The model of the item's newest item-level vector (a still's only vector, a video's mean, D228), else of its
    // newest keyframe vector. Until a model switch exists (E06-S07) an item has one model; then items whose vectors are
    // all of an outdated model will count as not analyzed (D051).
    private static Id<ModelRegistration>? PickModel(IReadOnlyList<Embedding> embeddings) =>
        embeddings
            .Where(embedding => embedding.Kind == EmbeddingKind.Image)
            .OrderBy(embedding => embedding.KeyframeId is null ? 0 : 1)
            .ThenByDescending(embedding => embedding.CreatedAt)
            .Select(embedding => (Id<ModelRegistration>?)embedding.ModelId)
            .FirstOrDefault();

    private static MediaSearchResults Empty(SemanticSearchStatus status) => new([], 0, [], SearchMode.Similar, status);
}
