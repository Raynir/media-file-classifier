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
            AnalysisScope.FolderScope folder => await ByIdsAsync(
                library,
                await library.Items.GetFilteredIdsAsync(new MediaItemFilter { FolderId = folder.FolderId, Subfolder = folder.Subfolder }, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false),
            AnalysisScope.PendingScope => await ByIdsAsync(
                library,
                await library.Items.GetFilteredIdsAsync(new MediaItemFilter { Status = MediaItemStatus.Pending }, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false),
            AnalysisScope.SearchResultsScope => await ByIdsAsync(library, await ViewIdsAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
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

    // The summaries of the given items, in the order of the ids (the repository answers in no particular order).
    // Items that were deleted since the ids were read are left out.
    private static async Task<List<MediaItemSummary>> ByIdsAsync(IOpenLibrary library, IReadOnlyList<Id<MediaItem>> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var found = new Dictionary<Id<MediaItem>, MediaItemSummary>(distinct.Count);
        foreach (var chunk in distinct.Chunk(PageSize))
        {
            foreach (var summary in await library.Items.GetSummariesByIdsAsync(chunk, cancellationToken).ConfigureAwait(false))
            {
                found[summary.Id] = summary;
            }
        }

        return [.. distinct.Where(found.ContainsKey).Select(id => found[id])];
    }

    // The ids of the current view. A search answers from the list of hits it read once for the search, so the order
    // holds while the pages are read; browsing is by offset, where an item added meanwhile can only repeat one (the
    // caller removes repeats), never hide one. An item deleted meanwhile can still shift the offset: a rare case.
    private async Task<List<Id<MediaItem>>> ViewIdsAsync(CancellationToken cancellationToken)
    {
        var all = new List<Id<MediaItem>>();
        while (true)
        {
            var batch = await view.GetIdsAsync(all.Count, PageSize, null, cancellationToken).ConfigureAwait(false);
            all.AddRange(batch);
            if (batch.Count < PageSize)
            {
                return all;
            }
        }
    }
}
