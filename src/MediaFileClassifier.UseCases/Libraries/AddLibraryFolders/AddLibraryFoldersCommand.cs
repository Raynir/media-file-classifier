using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;

/// <summary>
/// Adds folders to the open library (E02-S01-T03, E02-S03-T01) and imports them in the background (E03-S07). Overlaps
/// follow E02-S03-T02: a folder that's indexed already, or inside an indexed folder, is refused; indexed folders
/// inside a new folder are merged into it, keeping their items and metadata.
/// </summary>
/// <param name="Folders">Folders the user picked.</param>
public sealed record AddLibraryFoldersCommand(IReadOnlyList<FolderHandle> Folders) : IRequest<AddLibraryFoldersResult>;

/// <summary>
/// What adding folders did.
/// </summary>
/// <param name="Added">The new folders.</param>
/// <param name="Refused">The picked folders that were refused, each with the indexed folder that covers it.</param>
/// <param name="Merged">The indexed folders merged into a new folder.</param>
public sealed record AddLibraryFoldersResult(
    IReadOnlyList<IndexedFolder> Added,
    IReadOnlyList<(FolderHandle Folder, IndexedFolder CoveredBy)> Refused,
    IReadOnlyList<IndexedFolder> Merged);

/// <summary>
/// Handles <see cref="AddLibraryFoldersCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Compares folder handles.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="sender">Starts the import of the added folders, or <c>null</c> to only add them.</param>
/// <param name="jobs">Cancels the work of merged folders and queues their full hashes, if the platform has background processing.</param>
/// <param name="hashJobs">Queues full hashes.</param>
public sealed class AddLibraryFoldersCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider timeProvider,
    ILogger<AddLibraryFoldersCommandHandler> logger,
    ISender? sender = null,
    IJobSystem? jobs = null,
    FullHashJobs? hashJobs = null) : IRequestHandler<AddLibraryFoldersCommand, AddLibraryFoldersResult>
{
    /// <inheritdoc />
    public async Task<AddLibraryFoldersResult> Handle(AddLibraryFoldersCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        List<IndexedFolder> indexed = [.. await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false)];
        var added = new List<IndexedFolder>();
        var refused = new List<(FolderHandle, IndexedFolder)>();
        var merged = new List<IndexedFolder>();
        foreach (var handle in request.Folders)
        {
            var overlap = IndexedFolders.FindOverlap(files, handle, indexed);
            if (overlap.Kind is FolderOverlapKind.AlreadyIndexed or FolderOverlapKind.InsideIndexed)
            {
                refused.Add((handle, overlap.Folders[0]));
                continue;
            }

            // On the desktop the handle is a full path, which is the clearer thing to show.
            var displayPath = Path.IsPathFullyQualified(handle.Id) ? handle.Id : handle.DisplayName;
            var folder = IndexedFolder.Create(library.Library.Id, displayPath, handle.Id, timeProvider.GetUtcNow());
            await library.Folders.AddAsync(folder, cancellationToken).ConfigureAwait(false);
            foreach (var child in overlap.Folders)
            {
                await MergeAsync(library, folder, child, files.GetPathInside(child.ToHandle(), handle)!, cancellationToken).ConfigureAwait(false);
                indexed.Remove(child);
                merged.Add(child);
            }

            indexed.Add(folder);
            added.Add(folder);
        }

        if (added.Count > 0 && sender is not null)
        {
            await sender.Send(new ImportFoldersCommand([.. added.Select(folder => folder.Id)]), cancellationToken).ConfigureAwait(false);
        }

        return new AddLibraryFoldersResult(added, refused, merged);
    }

    private async Task MergeAsync(IOpenLibrary library, IndexedFolder parent, IndexedFolder child, string relativePath, CancellationToken cancellationToken)
    {
        // The child's jobs would write to a folder that's gone.
        var scheduler = jobs?.Current;
        if (scheduler is not null)
        {
            foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false)).Where(job => job.FolderId == child.Id))
            {
                await scheduler.CancelJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        // Its excluded subfolders stay excluded. Its other rules give way to the parent's.
        parent.ChangeRules(parent.Rules with { ExcludePatterns = [.. parent.Rules.ExcludePatterns, .. child.Rules.ExcludePatternsUnder(relativePath)] });
        var moved = await library.Folders.MergeIntoAsync(parent, child.Id, relativePath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Merged {Child} into {Parent}: {Count} items moved", child.DisplayPath, parent.DisplayPath, moved);

        // The parent's import queues the items still Discovered; the full hashes the cancelled job didn't make are queued here.
        if (scheduler is not null && hashJobs is not null)
        {
            var unhashed = (await library.Items.GetByFolderAsync(parent.Id, cancellationToken).ConfigureAwait(false))
                .Where(item => item.Status is MediaItemStatus.Pending or MediaItemStatus.Indexed && item.Hashes.ContentHash is null)
                .Select(item => item.Id)
                .ToList();
            if (unhashed.Count > 0)
            {
                await hashJobs.EnqueueAsync(scheduler, parent, unhashed, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
