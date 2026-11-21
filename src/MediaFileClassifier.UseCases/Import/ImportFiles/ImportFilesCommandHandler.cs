using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Exclusions;
using MediaFileClassifier.UseCases.Exclusions.RestoreExcludedItems;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.ImportFiles;

/// <summary>
/// Handles <see cref="ImportFilesCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// A file goes into the indexed folder that covers it: a full folder before a partial one, then the deepest. A file no
/// folder covers gets a new partial folder for its parent folder, shared by the other files there (D012).
/// </para>
/// <para>
/// The files are registered without scanning their folder (<see cref="IFolderDiscovery.RegisterFilesAsync"/>), and
/// each folder's new items are queued as one import job that starts at <see cref="ImportStages.Fingerprint"/>. In a
/// full folder whose import is still running, the files are left to it, so the two don't register the same path.
/// Without the job system the items stay Discovered, and the folder's next import queues them.
/// </para>
/// </remarks>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Compares folder handles.</param>
/// <param name="discovery">Registers the files.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="sender">Restores files the user had removed, or <c>null</c> to leave them removed.</param>
/// <param name="jobs">Runs the import, if the platform has background processing.</param>
public sealed class ImportFilesCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    IFolderDiscovery discovery,
    TimeProvider clock,
    ILogger<ImportFilesCommandHandler> logger,
    ISender? sender = null,
    IJobSystem? jobs = null) : IRequestHandler<ImportFilesCommand, ImportFilesResult>
{
    /// <inheritdoc />
    public async Task<ImportFilesResult> Handle(ImportFilesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        List<IndexedFolder> indexed = [.. await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false)];
        var added = new List<IndexedFolder>();
        var byFolder = new Dictionary<Id<IndexedFolder>, (IndexedFolder Folder, List<(FileLocation File, string Path)> Files)>();
        foreach (var file in request.Files.Distinct())
        {
            var (folder, path) = Resolve(file, indexed);
            if (folder is null)
            {
                // On the desktop the handle is a full path, which is the clearer thing to show.
                var displayPath = Path.IsPathFullyQualified(file.Folder.Id) ? file.Folder.Id : file.Folder.DisplayName;
                folder = IndexedFolder.CreateForSelectedFiles(library.Library.Id, displayPath, file.Folder.Id, clock.GetUtcNow());
                folder.ChangeVolumeLabel(await files.GetVolumeLabelAsync(file.Folder, cancellationToken).ConfigureAwait(false));
                await library.Folders.AddAsync(folder, cancellationToken).ConfigureAwait(false);
                indexed.Add(folder);
                added.Add(folder);
                path = file.RelativePath;
                logger.LogInformation("Added {Folder} as a partial folder", folder.DisplayPath);
            }

            if (!byFolder.TryGetValue(folder.Id, out var entry))
            {
                entry = (folder, []);
                byFolder[folder.Id] = entry;
            }

            entry.Files.Add((file, path));
        }

        var importing = (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Type == ImportStages.ImportJob && job.FolderId is not null)
            .Select(job => job.FolderId!.Value)
            .ToHashSet();
        var result = new Outcomes();
        foreach (var (folder, folderFiles) in byFolder.Values)
        {
            if (!folder.SelectedFilesOnly && importing.Contains(folder.Id))
            {
                result.Imported.AddRange(folderFiles.Select(entry => entry.File));
                continue;
            }

            await RegisterAsync(library, folder, folderFiles, result, cancellationToken).ConfigureAwait(false);
        }

        if (result.Removed.Count > 0 && sender is not null)
        {
            await sender.Send(new RestoreExcludedItemsCommand([.. result.Removed.Select(entry => entry.ItemId)]), cancellationToken).ConfigureAwait(false);
        }

        result.Imported.AddRange(result.Removed.Select(entry => entry.File));
        logger.LogInformation(
            "Import of {Count} selected files: {Imported} importing, {Already} in the library, {Excluded} excluded by rules, {Unsupported} unsupported, {Unreadable} unreadable, {Folders} partial folders added",
            request.Files.Count,
            result.Imported.Count,
            result.AlreadyInLibrary.Count,
            result.ExcludedByRules.Count,
            result.Unsupported.Count,
            result.Unreadable.Count,
            added.Count);
        return new ImportFilesResult
        {
            Imported = result.Imported,
            AlreadyInLibrary = result.AlreadyInLibrary,
            ExcludedByRules = result.ExcludedByRules,
            Unsupported = result.Unsupported,
            Unreadable = result.Unreadable,
            AddedFolders = added,
        };
    }

    private (IndexedFolder? Folder, string Path) Resolve(FileLocation file, IReadOnlyList<IndexedFolder> indexed)
    {
        var covering = indexed
            .Select(folder => (Folder: folder, Inside: files.GetPathInside(file.Folder, folder.ToHandle())))
            .Where(match => match.Inside is not null)
            .OrderBy(match => match.Folder.SelectedFilesOnly)
            .ThenBy(match => match.Inside!.Length)
            .FirstOrDefault();
        if (covering.Folder is null)
        {
            return (null, file.RelativePath);
        }

        return (covering.Folder, covering.Inside!.Length == 0 ? file.RelativePath : covering.Inside.TrimEnd('/') + "/" + file.RelativePath);
    }

    private async Task RegisterAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        List<(FileLocation File, string Path)> folderFiles,
        Outcomes result,
        CancellationToken cancellationToken)
    {
        var registrations = await discovery.RegisterFilesAsync(library, folder, [.. folderFiles.Select(entry => entry.Path)], cancellationToken).ConfigureAwait(false);
        var byPath = registrations.ToDictionary(registration => registration.RelativePath, StringComparer.Ordinal);
        var registered = new List<Id<MediaItem>>();
        foreach (var (file, path) in folderFiles)
        {
            var registration = byPath[path];
            switch (registration.Outcome)
            {
                case FileRegistrationOutcome.Registered:
                    result.Imported.Add(file);
                    registered.Add(registration.Item!.Id);
                    break;
                case FileRegistrationOutcome.AlreadyInLibrary:
                    var item = await library.Items.FindByPathAsync(folder.Id, RelativePath.From(path), cancellationToken).ConfigureAwait(false);
                    if (item is not null && LibraryExclusions.IsRemovedByUser(item))
                    {
                        result.Removed.Add((file, item.Id));
                    }
                    else
                    {
                        result.AlreadyInLibrary.Add(file);
                    }

                    break;
                case FileRegistrationOutcome.ExcludedByRules:
                    result.ExcludedByRules.Add(file);
                    break;
                case FileRegistrationOutcome.Unsupported:
                    result.Unsupported.Add(file);
                    break;
                default:
                    result.Unreadable.Add(file);
                    break;
            }
        }

        if (registered.Count > 0 && jobs?.Current is { } scheduler)
        {
            await scheduler.EnqueueJobAsync(
                new JobRequest(
                    ImportStages.ImportJob,
                    $"Import {registered.Count} files into {folder.DisplayPath}",
                    JobPriority.Import,
                    [.. registered.Select(itemId => new WorkItemRequest(ImportStages.Fingerprint, itemId))])
                {
                    FolderId = folder.Id,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Outcomes
    {
        public List<FileLocation> Imported { get; } = [];

        public List<FileLocation> AlreadyInLibrary { get; } = [];

        public List<(FileLocation File, Id<MediaItem> ItemId)> Removed { get; } = [];

        public List<FileLocation> ExcludedByRules { get; } = [];

        public List<FileLocation> Unsupported { get; } = [];

        public List<FileLocation> Unreadable { get; } = [];
    }
}
