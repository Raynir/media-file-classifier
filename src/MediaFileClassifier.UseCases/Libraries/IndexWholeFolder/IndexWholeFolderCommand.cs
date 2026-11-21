using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.IndexWholeFolder;

/// <summary>
/// Makes a partial ("selected files only") folder a full folder and imports it (E03-S05-T03). Full discovery finds the
/// items the folder already has by their paths, so they keep their IDs and metadata; the other files are imported.
/// Indexed folders inside it are merged into it first, as Add folder does, so their files aren't listed twice.
/// </summary>
/// <param name="FolderId">The partial folder.</param>
public sealed record IndexWholeFolderCommand(Id<IndexedFolder> FolderId) : IRequest<bool>;

/// <summary>
/// Handles <see cref="IndexWholeFolderCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="sender">Starts the import.</param>
/// <param name="files">Compares folder handles.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Cancels the folder's running import, if the platform has background processing.</param>
/// <param name="hashJobs">Queues the full hashes of merged folders' items.</param>
public sealed class IndexWholeFolderCommandHandler(
    ILibrarySession session,
    ISender sender,
    IMediaFileSource files,
    ILogger<IndexWholeFolderCommandHandler> logger,
    IJobSystem? jobs = null,
    FullHashJobs? hashJobs = null) : IRequestHandler<IndexWholeFolderCommand, bool>
{
    /// <inheritdoc />
    /// <returns><see langword="false"/> if the folder was a full folder already.</returns>
    public async Task<bool> Handle(IndexWholeFolderCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var folder = await library.Folders.GetAsync(request.FolderId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("There's no such folder.", nameof(request));
        if (!folder.SelectedFilesOnly)
        {
            return false;
        }

        // A running import of the picked files would keep the full import from starting; discovery queues its items again.
        if (jobs?.Current is { } scheduler)
        {
            foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
                .Where(job => job.FolderId == folder.Id && job.Type == ImportStages.ImportJob))
            {
                await scheduler.CancelJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        folder.IndexWholeFolder();
        await library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
        var others = await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false);
        var merged = await new FolderMerging(logger, jobs, hashJobs).MergeContainedAsync(library, files, folder, others, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Indexing the whole of {Folder}, {Merged} folders merged into it", folder.DisplayPath, merged.Count);

        await sender.Send(new ImportFoldersCommand([folder.Id]), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
