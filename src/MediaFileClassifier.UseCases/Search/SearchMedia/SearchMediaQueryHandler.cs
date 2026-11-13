using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Search.SearchMedia;

/// <summary>
/// Runs <see cref="SearchMediaQuery"/> (E09-S01-T01): encodes the text with the text tower, then hands text and
/// vector to the search index, which runs the keyword and vector searches in parallel and fuses them. When the
/// semantic side can't run, the keyword results come back alone with the reason. A reference image (E09-S04) is
/// encoded with the image tower instead and searched by vector alone; when it can't be, there are no results.
/// </summary>
/// <param name="index">The search index.</param>
/// <param name="encoder">The query encoder.</param>
/// <param name="registry">The AI providers, to find the text model's relevance threshold.</param>
/// <param name="filters">The registered filters, for the request's <see cref="SearchMediaQuery.Filters"/>.</param>
/// <param name="logger">Logger.</param>
public sealed class SearchMediaQueryHandler(
    ISearchIndex index,
    SemanticQueryEncoder encoder,
    IAiProviderRegistry registry,
    FilterRegistry filters,
    ILogger<SearchMediaQueryHandler> logger) : IRequestHandler<SearchMediaQuery, MediaSearchResults>
{
    /// <inheritdoc />
    public async Task<MediaSearchResults> Handle(SearchMediaQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ReferenceImage is { } image)
        {
            return await SearchByImageAsync(request, image, cancellationToken).ConfigureAwait(false);
        }

        var text = SearchText.Normalise(request.Text);
        if (text.Length == 0)
        {
            var listed = await index.SearchAsync(
                new SearchQuery
                {
                    Filter = request.Filters.ApplyTo(request.Filter, filters),
                    Sort = request.Sort == SortMode.Relevance ? SortMode.Date : request.Sort,
                    ReverseSort = request.ReverseSort,
                    Offset = request.Offset,
                    Limit = request.Limit,
                    IncludeMatchedFields = false,
                },
                cancellationToken).ConfigureAwait(false);
            return new MediaSearchResults(listed.Hits, listed.TotalCount, [], SearchMode.Browse, SemanticSearchStatus.NotNeeded);
        }

        var (vector, status) = await EncodeAsync(text, cancellationToken).ConfigureAwait(false);
        var results = await index.SearchAsync(
            new SearchQuery
            {
                Text = text,
                Vector = vector,
                Filter = request.Filters.ApplyTo(request.Filter, filters),
                Sort = request.Sort,
                ReverseSort = request.ReverseSort,
                Offset = request.Offset,
                Limit = request.Limit,
                IncludeMatchedFields = request.IncludeMatchedFields,
                RelevanceCut = vector is null ? null : FindCut(),
                IncludeLessRelevant = request.ShowLessRelevant,
            },
            cancellationToken).ConfigureAwait(false);

        if (results.VectorSearchSkipped)
        {
            status = SemanticSearchStatus.IndexNotReady;
        }

        var mode = (status == SemanticSearchStatus.Used, results.Terms.Count > 0) switch
        {
            (true, true) => SearchMode.Hybrid,
            (true, false) => SearchMode.Semantic,
            _ => SearchMode.Keyword,
        };
        return new MediaSearchResults(results.Hits, results.TotalCount, results.Terms, mode, status, results.LessRelevantCount);
    }

    // No relevance cut (decided for E09-S04): the text thresholds don't fit image-to-image similarities, which are much
    // higher; E09-S05 measures one. The index returns at most its vector candidate limit, ranked by similarity.
    private async Task<MediaSearchResults> SearchByImageAsync(SearchMediaQuery request, ReferenceImage image, CancellationToken cancellationToken)
    {
        var (vector, status) = await EncodeImageAsync(image, cancellationToken).ConfigureAwait(false);
        if (vector is null)
        {
            return new MediaSearchResults([], 0, [], SearchMode.Image, status);
        }

        var results = await index.SearchAsync(
            new SearchQuery
            {
                Vector = vector,
                Filter = request.Filters.ApplyTo(request.Filter, filters),
                Sort = request.Sort,
                ReverseSort = request.ReverseSort,
                Offset = request.Offset,
                Limit = request.Limit,
            },
            cancellationToken).ConfigureAwait(false);

        // Without a vector the index would list every item, which isn't an answer to "find this picture".
        return results.VectorSearchSkipped
            ? new MediaSearchResults([], 0, [], SearchMode.Image, SemanticSearchStatus.IndexNotReady)
            : new MediaSearchResults(results.Hits, results.TotalCount, [], SearchMode.Image, SemanticSearchStatus.Used);
    }

    // The same provider the encoder picks: the first text embedder.
    private SemanticRelevanceCut? FindCut() =>
        registry.GetProviders<ITextEmbeddingProvider>().FirstOrDefault() is { } provider ? SemanticRelevanceThresholds.For(provider.Model) : null;

    private async Task<(SearchVector? Vector, SemanticSearchStatus Status)> EncodeAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var vector = await encoder.EncodeTextAsync(text, cancellationToken).ConfigureAwait(false);
            return vector is null ? (null, SemanticSearchStatus.Unavailable) : (vector, SemanticSearchStatus.Used);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.ModelMissing)
        {
            return (null, SemanticSearchStatus.ModelMissing);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(exception, "Encoding a search query failed; searching by keyword only");
            return (null, SemanticSearchStatus.Failed);
        }
    }

    private async Task<(SearchVector? Vector, SemanticSearchStatus Status)> EncodeImageAsync(ReferenceImage image, CancellationToken cancellationToken)
    {
        try
        {
            var vector = await encoder.EncodeImageAsync(image.Image, cancellationToken).ConfigureAwait(false);
            return vector is null ? (null, SemanticSearchStatus.Unavailable) : (vector, SemanticSearchStatus.Used);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.ModelMissing)
        {
            return (null, SemanticSearchStatus.ModelMissing);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(exception, "Encoding the reference image {Name} failed", image.Name);
            return (null, SemanticSearchStatus.Failed);
        }
    }
}
