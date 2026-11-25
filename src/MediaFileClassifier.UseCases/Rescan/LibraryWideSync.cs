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
}
