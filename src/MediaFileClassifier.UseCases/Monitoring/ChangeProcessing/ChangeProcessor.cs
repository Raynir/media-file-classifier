using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Import;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// Queues a watched folder's changes (E04-S02) in the job system, as <see cref="ChangeProcessingStages.Apply"/> work in
/// an import job of the folder at import priority. Nothing is applied here, so processing that's paused (E05-S05) holds
/// the changes too.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Runs the jobs of the open library, if the platform has background processing.</param>
public sealed partial class ChangeProcessor(ILogger<ChangeProcessor> logger, IJobSystem? jobs = null)
{
    /// <summary>
    /// The most changes in one work item, so a renamed folder of many files is applied in a few steps.
    /// </summary>
    public const int ChangesPerWorkItem = 1000;

    /// <summary>
    /// Queues a folder's changes.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="changes">The changes, in the order they became stable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are queued.</returns>
    public async Task SubmitAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return;
        }

        if (jobs?.Current is not { } scheduler)
        {
            LogNoJobSystem(changes.Count, folderId);
            return;
        }

        var folder = await scheduler.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return;
        }

        WorkItemRequest[] work = [.. changes.Chunk(ChangesPerWorkItem)
            .Select(chunk => new WorkItemRequest(ChangeProcessingStages.Apply, Payload: ChangePayload.Serialize(chunk)))];
        await scheduler.EnqueueJobAsync(
            new JobRequest(ImportStages.ImportJob, $"Import changes in {folder.DisplayPath}", JobPriority.Import, work) { FolderId = folderId },
            cancellationToken).ConfigureAwait(false);
        LogQueued(changes.Count, folderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes of watched folder {Folder} aren't processed: no library is open or there's no job system")]
    private partial void LogNoJobSystem(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued {Count} changes of watched folder {Folder}")]
    private partial void LogQueued(int count, Id<IndexedFolder> folder);
}
