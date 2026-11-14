using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Browsing;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Finds the items of an <see cref="AnalysisScope"/> and counts them by media type, so a bulk analysis can show
/// how many items and about how long before it starts (E06-S06-T02, T03).
/// </summary>
/// <param name="Scope">The scope.</param>
public sealed record ResolveAnalysisScopeQuery(AnalysisScope Scope) : IRequest<AnalysisScopeItems>;

/// <summary>
/// Handles <see cref="ResolveAnalysisScopeQuery"/>.
/// </summary>
/// <param name="session">The open library.</param>
/// <param name="view">The items of the current view: the search results when searching.</param>
public sealed class ResolveAnalysisScopeQueryHandler(ILibrarySession session, IMediaResultSource view)
    : IRequestHandler<ResolveAnalysisScopeQuery, AnalysisScopeItems>
{
    private const int PageSize = 1000;

    /// <inheritdoc />
    public async Task<AnalysisScopeItems> Handle(ResolveAnalysisScopeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (session.Current is not { } library)
        {
            return AnalysisScopeItems.Empty;
        }

        var summaries = request.Scope switch
        {
            AnalysisScope.ItemsScope items => await ByIdsAsync(library, [.. items.ItemIds.Distinct()], cancellationToken).ConfigureAwait(false),
            AnalysisScope.FolderScope folder => await PagesAsync(
                (offset, limit, token) => library.Items.GetSummariesAsync(new MediaItemFilter { FolderId = folder.FolderId, Subfolder = folder.Subfolder }, offset, limit, token),
                cancellationToken).ConfigureAwait(false),
            AnalysisScope.PendingScope => await PagesAsync(
                (offset, limit, token) => library.Items.GetSummariesAsync(new MediaItemFilter { Status = MediaItemStatus.Pending }, offset, limit, token),
                cancellationToken).ConfigureAwait(false),
            AnalysisScope.SearchResultsScope => await PagesAsync((offset, limit, token) => view.GetPageAsync(offset, limit, token), cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Unknown analysis scope.", nameof(request)),
        };

        var present = summaries.Where(summary => summary.Status != MediaItemStatus.Missing).ToList();
        return new AnalysisScopeItems(
            present.ConvertAll(summary => summary.Id),
            present.Count(summary => summary.MediaType != MediaType.Video),
            present.Count(summary => summary.MediaType == MediaType.Video),
            present.Where(summary => summary.MediaType == MediaType.Video).Sum(VideoFrames));
    }

    // A video with an unknown duration is read at its start only, as the frame sampler does.
    private static int VideoFrames(MediaItemSummary video) =>
        video.Duration is { } duration && duration > TimeSpan.Zero ? FrameSamplingOptions.Default.FrameCount(duration) : 1;

    private static async Task<List<MediaItemSummary>> ByIdsAsync(IOpenLibrary library, List<Id<MediaItem>> ids, CancellationToken cancellationToken)
    {
        var all = new List<MediaItemSummary>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
        {
            all.AddRange(await library.Items.GetSummariesByIdsAsync(chunk, cancellationToken).ConfigureAwait(false));
        }

        return all;
    }

    private static async Task<List<MediaItemSummary>> PagesAsync(
        Func<int, int, CancellationToken, Task<IReadOnlyList<MediaItemSummary>>> page,
        CancellationToken cancellationToken)
    {
        var all = new List<MediaItemSummary>();
        while (true)
        {
            var batch = await page(all.Count, PageSize, cancellationToken).ConfigureAwait(false);
            all.AddRange(batch);
            if (batch.Count < PageSize)
            {
                return all;
            }
        }
    }
}
