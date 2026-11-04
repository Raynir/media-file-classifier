using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.RemoveFolder;

/// <summary>
/// Removes a folder from the library (E02-S03-T04): its items, their metadata, embeddings and thumbnails go, and the
/// files on disk are never touched. The folder's other work is cancelled, and the removal runs as a background job,
/// so a large folder doesn't block the UI; the folder isn't listed from the start.
/// </summary>
/// <param name="FolderId">The folder.</param>
public sealed record RemoveFolderCommand(Id<IndexedFolder> FolderId) : IRequest<bool>;

/// <summary>
/// Handles <see cref="RemoveFolderCommand"/>. Without background processing the folder is removed before it returns.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="thumbnails">Deletes the thumbnails.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Runs the removal, if the platform has background processing.</param>
public sealed class RemoveFolderCommandHandler(
    ILibrarySession session,
    IThumbnailCache thumbnails,
    ILogger<RemoveFolderCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<RemoveFolderCommand, bool>
{
    /// <inheritdoc />
    /// <returns><see langword="false"/> if there's no such folder, or it's being removed already.</returns>
    public async Task<bool> Handle(RemoveFolderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var folder = (await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(folder => folder.Id == request.FolderId);
        if (folder is null)
        {
            return false;
        }

        if (jobs?.Current is not { } scheduler)
        {
            await RemoveFolderStage.RemoveAsync(library, thumbnails, folder.Id, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Removed {Folder} from the library", folder.DisplayPath);
            return true;
        }

        foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false)).Where(job => job.FolderId == folder.Id))
        {
            await scheduler.CancelJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
        }

        await scheduler.EnqueueJobAsync(
            new JobRequest(IndexedFolders.RemoveJob, $"Remove {folder.DisplayPath}", JobPriority.UserAction, [new WorkItemRequest(IndexedFolders.RemoveStage)])
            {
                FolderId = folder.Id,
            },
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Queued the removal of {Folder}", folder.DisplayPath);
        return true;
    }
}
