using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.ImportFolders;

/// <summary>
/// Starts importing folders of the open library in the background (E03-S07): one <see cref="ImportStages.ImportJob"/>
/// job per folder, which begins with <see cref="ImportStages.Discover"/>.
/// </summary>
/// <param name="FolderIds">The folders.</param>
public sealed record ImportFoldersCommand(IReadOnlyList<Id<IndexedFolder>> FolderIds) : IRequest<IReadOnlyList<Id<ProcessingJob>>>;

/// <summary>
/// Handles <see cref="ImportFoldersCommand"/>. A folder whose import is still unfinished isn't queued again. Nothing is
/// queued where no background processing runs: on a platform without the job system, or with no library open.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Runs the jobs of the open library, if the platform has background processing.</param>
public sealed class ImportFoldersCommandHandler(ILogger<ImportFoldersCommandHandler> logger, IJobSystem? jobs = null)
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
            .Where(job => job.Type == ImportStages.ImportJob && job.FolderId is not null)
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
