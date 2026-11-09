using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Embeddings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Search.SearchMedia;

/// <summary>
/// Runs <see cref="SearchMediaQuery"/> (E09-S01-T01): encodes the text with the text tower, then hands text and
/// vector to the search index, which runs the keyword and vector searches in parallel and fuses them. When the
/// semantic side can't run, the keyword results come back alone with the reason.
/// </summary>
/// <param name="index">The search index.</param>
/// <param name="encoder">The query encoder.</param>
/// <param name="logger">Logger.</param>
public sealed class SearchMediaQueryHandler(
    ISearchIndex index,
    SemanticQueryEncoder encoder,
    ILogger<SearchMediaQueryHandler> logger) : IRequestHandler<SearchMediaQuery, MediaSearchResults>
{
    /// <inheritdoc />
    public async Task<MediaSearchResults> Handle(SearchMediaQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            var listed = await index.SearchAsync(
                new SearchQuery
                {
                    Filter = request.Filter,
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
                Filter = request.Filter,
                Sort = request.Sort,
                ReverseSort = request.ReverseSort,
                Offset = request.Offset,
                Limit = request.Limit,
                IncludeMatchedFields = request.IncludeMatchedFields,
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
        return new MediaSearchResults(results.Hits, results.TotalCount, results.Terms, mode, status);
    }

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
}
