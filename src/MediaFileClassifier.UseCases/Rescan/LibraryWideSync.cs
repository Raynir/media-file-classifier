using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// Whether a library-wide rescan or quick sync will list every folder (MFC-710, MFC-1049): one that's unfinished, e.g.
/// resumed from the last session or started by the user, so queueing another sync behind it would list them twice.
/// </summary>
internal static class LibraryWideSync
{
    /// <summary>
    /// Tells whether a library-wide rescan or quick sync is unfinished. One waiting for a model has done its listing,
    /// as for <see cref="StartRescan.StartRescanCommandHandler"/>.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if one is unfinished.</returns>
    public static async Task<bool> IsUnfinishedAsync(IOpenLibrary library, CancellationToken cancellationToken) =>
        (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false)).Any(job =>
            job is { FolderId: null, Type: RescanStages.RescanJob or RescanStages.QuickSyncJob }
            && job.State != ProcessingJobState.WaitingForModel);

    /// <summary>
    /// Tells whether a library-wide rescan or quick sync is yet to list the folders: its comparison with the library is
    /// still queued, so it reads the folders, with their statuses, when it starts (MFC-1049). Once that has run, a
    /// folder that came back Online since isn't in its listing.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if one will list the folders.</returns>
    public static async Task<bool> WillListAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job is { FolderId: null, Type: RescanStages.RescanJob or RescanStages.QuickSyncJob }))
        {
            if ((await library.Jobs.GetWorkItemsAsync(job.Id, cancellationToken).ConfigureAwait(false))
                .Any(item => item.Stage == RescanStages.Reconcile && item.State == WorkItemState.Queued))
            {
                return true;
            }
        }

        return false;
    }
}
