using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Browsing;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Finds the items of an <see cref="AnalysisScope"/> and counts them by media type, so a bulk analysis can show
/// how many items and about how long before it starts (E06-S06-T02, T03).
/// </summary>
/// <param name="Scope">The scope.</param>
/// <param name="Analyzers">
/// The analyzers to run, or <see langword="null"/> for the whole profile. With the outdated scope, only items outdated in
/// one of them are found, and each runs its outdated ones (<see cref="AnalysisScopeItems.ItemAnalyzers"/>).
/// </param>
public sealed record ResolveAnalysisScopeQuery(AnalysisScope Scope, IReadOnlyCollection<string>? Analyzers = null) : IRequest<AnalysisScopeItems>;

/// <summary>
/// Handles <see cref="ResolveAnalysisScopeQuery"/>.
/// </summary>
/// <param name="session">The open library.</param>
/// <param name="view">The items of the current view: the search results when searching.</param>
/// <param name="registry">The provider registry, for the outdated scope; without one nothing is outdated.</param>
public sealed class ResolveAnalysisScopeQueryHandler(ILibrarySession session, IMediaResultSource view, IAiProviderRegistry? registry = null)
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

        Dictionary<Id<MediaItem>, IReadOnlyList<string>>? outdated = null;
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
            AnalysisScope.LibraryScope => await ByIdsAsync(
                library,
                await library.Items.GetFilteredIdsAsync(new MediaItemFilter(), cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false),
            AnalysisScope.OutdatedScope => await ByIdsAsync(
                library,
                [.. (outdated = await OutdatedAsync(library, request.Analyzers, cancellationToken).ConfigureAwait(false)).Keys],
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Unknown analysis scope.", nameof(request)),
        };

        // Pending is read as a snapshot of ids, so an item that an analysis run finished meanwhile is dropped here, by
        // the status read with its summary: it isn't counted and isn't queued again (MFC-995).
        var present = summaries
            .Where(summary => summary.Status != MediaItemStatus.Missing
                && (request.Scope is not AnalysisScope.PendingScope || summary.Status == MediaItemStatus.Pending))
            .ToList();
        return new AnalysisScopeItems(
            present.ConvertAll(summary => summary.Id),
            present.Count(summary => summary.MediaType != MediaType.Video),
            present.Count(summary => summary.MediaType == MediaType.Video),
            present.Where(summary => summary.MediaType == MediaType.Video).Sum(VideoFrames))
        {
            ItemAnalyzers = outdated,
        };
    }

    // The outdated items, each with its outdated analyzers in the profile's order, limited to the chosen ones.
    private async Task<Dictionary<Id<MediaItem>, IReadOnlyList<string>>> OutdatedAsync(
        IOpenLibrary library,
        IReadOnlyCollection<string>? analyzers,
        CancellationToken cancellationToken)
    {
        if (registry is null)
        {
            return [];
        }

        var profile = library.Library.Settings.AnalysisProfile;
        var current = CurrentProvenance.Resolve(profile, registry)
            .Where(provenance => analyzers is null || analyzers.Contains(provenance.Analyzer, StringComparer.Ordinal))
            .ToList();
        var outdated = await library.AnalyzerStates.GetOutdatedAsync(current, cancellationToken).ConfigureAwait(false);
        var order = profile.Analyzers.Select(settings => settings.Analyzer).ToList();
        return outdated.ItemIds.ToDictionary(itemId => itemId, itemId => outdated.AnalyzersOf(itemId, order));
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

    // The ids of the current view, read in one go: a snapshot, so items deleted meanwhile (remove missing, removing a
    // folder) can't shift the pages and hide others (MFC-995, D329). A search answers from the list of hits it read once
    // for the search; browsing is one ordered read of the ids.
    private Task<IReadOnlyList<Id<MediaItem>>> ViewIdsAsync(CancellationToken cancellationToken) =>
        view.GetIdsAsync(0, int.MaxValue, null, cancellationToken);
}
