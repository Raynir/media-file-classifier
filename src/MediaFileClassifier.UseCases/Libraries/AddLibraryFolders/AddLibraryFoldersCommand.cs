using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediaFileClassifier.UseCases.Libraries.IndexWholeFolder;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;

/// <summary>
/// Adds folders to the open library (E02-S01-T03, E02-S03-T01) and imports them in the background (E03-S07). Overlaps
/// follow E02-S03-T02: a folder that's indexed already, or inside an indexed folder, is refused; indexed folders
/// inside a new folder are merged into it, keeping their items and metadata. A partial folder (E03-S05) is indexed
/// whole when it's added, and a folder added inside one takes the partial folder's items under it.
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
    IReadOnlyList<IndexedFolder> Merged)
{
    /// <summary>
    /// Gets the partial folders that were picked, which are indexed whole now (E03-S05).
    /// </summary>
    public IReadOnlyList<IndexedFolder> IndexedWhole { get; init; } = [];
}

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
        var indexedWhole = new List<IndexedFolder>();
        foreach (var handle in request.Folders)
        {
            var overlap = IndexedFolders.FindOverlap(files, handle, indexed);
            if (overlap.Kind is FolderOverlapKind.AlreadyIndexed or FolderOverlapKind.InsideIndexed)
            {
                refused.Add((handle, overlap.Folders[0]));
                continue;
            }

            if (overlap.Kind == FolderOverlapKind.PartialFolder)
            {
                // Without the sender nothing can index it, so it's covered as it is.
                if (sender is null)
                {
                    refused.Add((handle, overlap.Folders[0]));
                }
                else if (await sender.Send(new IndexWholeFolderCommand(overlap.Folders[0].Id), cancellationToken).ConfigureAwait(false))
                {
                    indexedWhole.Add(overlap.Folders[0]);
                }

                continue;
            }

            // Partial folders that contain it (picked files can make nested ones) keep their other files; folders
            // inside the new one are merged as usual.
            var partials = new List<IndexedFolder>();
            while (overlap.Kind == FolderOverlapKind.InsidePartial)
            {
                partials.Add(overlap.Folders[0]);
                overlap = IndexedFolders.FindOverlap(files, handle, [.. indexed.Where(folder => !partials.Contains(folder))]);
            }

            // On the desktop the handle is a full path, which is the clearer thing to show.
            var displayPath = Path.IsPathFullyQualified(handle.Id) ? handle.Id : handle.DisplayName;
            var folder = IndexedFolder.Create(library.Library.Id, displayPath, handle.Id, timeProvider.GetUtcNow());
            folder.ChangeVolumeLabel((await VolumeLabels.TryReadAsync(files, handle, logger, cancellationToken).ConfigureAwait(false)).Label);
            await library.Folders.AddAsync(folder, cancellationToken).ConfigureAwait(false);
            foreach (var partial in partials)
            {
                if (await SplitAsync(library, folder, partial, files.GetPathInside(handle, partial.ToHandle())!, cancellationToken).ConfigureAwait(false))
                {
                    indexed.Remove(partial);
                }
            }

            foreach (var child in overlap.Folders)
            {
                await Merging.MergeAsync(library, folder, child, files.GetPathInside(child.ToHandle(), handle)!, cancellationToken).ConfigureAwait(false);
                indexed.Remove(child);
                merged.Add(child);
            }

            indexed.Add(folder);
            added.Add(folder);
            if (sender is not null)
            {
                await sender.Send(
                    new RecordActivityCommand(
                        ActivityEventType.FolderAdded,
                        Subject: folder.DisplayPath,
                        Counts: overlap.Folders.Count == 0 ? null : new Dictionary<string, long> { [ActivityCounts.Folders] = overlap.Folders.Count },
                        FolderId: folder.Id),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (added.Count > 0 && sender is not null)
        {
            await sender.Send(new ImportFoldersCommand([.. added.Select(folder => folder.Id)]), cancellationToken).ConfigureAwait(false);
        }

        return new AddLibraryFoldersResult(added, refused, merged) { IndexedWhole = indexedWhole };
    }

    private FolderMerging Merging => new(logger, jobs, hashJobs);

    private async Task<bool> SplitAsync(IOpenLibrary library, IndexedFolder folder, IndexedFolder partial, string relativePath, CancellationToken cancellationToken)
    {
        // The partial folder's import may be working on the items that move; it's started again for the ones that stay.
        await Merging.CancelJobsAsync(library, partial, cancellationToken).ConfigureAwait(false);
        var (moved, deleted) = await library.Folders.SplitIntoAsync(folder, partial.Id, relativePath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Moved {Count} items of {Partial} to {Folder}", moved, partial.DisplayPath, folder.DisplayPath);
        await Merging.QueueFullHashesAsync(library, folder, cancellationToken).ConfigureAwait(false);
        if (!deleted && sender is not null)
        {
            await sender.Send(new ImportFoldersCommand([partial.Id]), cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }
}
