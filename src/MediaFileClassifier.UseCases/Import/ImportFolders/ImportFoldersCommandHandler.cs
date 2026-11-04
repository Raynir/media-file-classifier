using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.ImportFolders;

/// <summary>
/// Handles <see cref="ImportFoldersCommand"/>. A folder whose import is still unfinished, or that's being removed, isn't
/// queued again. A folder that can't be reached is marked Offline and isn't queued (E02-S03-T05). Nothing is queued
/// where no background processing runs: on a platform without the job system, or with no library open.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="clock">The clock.</param>
/// <param name="jobs">Runs the jobs of the open library, if the platform has background processing.</param>
/// <param name="files">Checks that the folders can be reached, or <c>null</c> to leave that to discovery.</param>
public sealed class ImportFoldersCommandHandler(
    ILogger<ImportFoldersCommandHandler> logger,
    TimeProvider clock,
    IJobSystem? jobs = null,
    IMediaFileSource? files = null)
    : IRequestHandler<ImportFoldersCommand, IReadOnlyList<Id<ProcessingJob>>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<ProcessingJob>>> Handle(ImportFoldersCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (jobs?.Current is not { } scheduler)
        {
            logger.LogWarning("Background processing isn't running, so {Count} folders aren't imported", request.FolderIds.Count);
            return [];
        }

        var unfinished = (await scheduler.Library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Type is ImportStages.ImportJob or IndexedFolders.RemoveJob && job.FolderId is not null)
            .Select(job => job.FolderId!.Value)
            .ToHashSet();
        var started = new List<Id<ProcessingJob>>();
        foreach (var folderId in request.FolderIds.Distinct())
        {
            var folder = await scheduler.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
            if (folder is null || unfinished.Contains(folderId))
            {
                continue;
            }

            if (files is not null && !await files.IsAvailableAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false))
            {
                if (folder.Status != FolderStatus.Offline)
                {
                    folder.MarkOffline(clock.GetUtcNow());
                    await scheduler.Library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation("{Folder} can't be reached, so it isn't imported", folder.DisplayPath);
                continue;
            }

            var jobId = await scheduler.EnqueueJobAsync(
                new JobRequest(ImportStages.ImportJob, $"Import {folder.DisplayPath}", JobPriority.Import, [new WorkItemRequest(ImportStages.Discover)])
                {
                    FolderId = folder.Id,
                },
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Queued the import of {Folder}", folder.DisplayPath);
            started.Add(jobId);
        }

        return started;
    }
}
