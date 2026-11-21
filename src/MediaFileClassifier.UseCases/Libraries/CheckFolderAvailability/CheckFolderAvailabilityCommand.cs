using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Checks whether the open library's folders can be reached, and marks them Online or Offline (E02-S03-T05). It runs
/// when a library opens, before imports and when the folders view opens. An Offline folder's items are kept as they
/// are: nothing is marked Missing because its root can't be reached. When a folder is Online again, the work parked
/// for it runs (D025). An Online folder's volume label is read again, so an Offline one can name its drive
/// (E14-S07-T02). It also runs when a volume arrives or goes away, and every 30 s for the Offline folders alone
/// (E14-S07-T04, <see cref="FolderAvailabilityMonitor"/>); when a status changes, the folder repository's
/// <c>StatusChanged</c> tells the browser through <see cref="FolderStatusChanges"/>. Only the availability columns are
/// stored, so a relink or rule change made while the probes run isn't undone.
/// </summary>
/// <param name="FolderIds">The folders to check, or <c>null</c> for all of them.</param>
/// <param name="OfflineOnly">Whether to check only the folders that are Offline now, among <paramref name="FolderIds"/>.</param>
public sealed record CheckFolderAvailabilityCommand(IReadOnlyCollection<Id<IndexedFolder>>? FolderIds = null, bool OfflineOnly = false) : IRequest<IReadOnlyList<IndexedFolder>>;

/// <summary>
/// Handles <see cref="CheckFolderAvailabilityCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Checks the folders.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Releases the parked work when a folder is Online again, or <see langword="null"/>.</param>
public sealed class CheckFolderAvailabilityCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<CheckFolderAvailabilityCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<CheckFolderAvailabilityCommand, IReadOnlyList<IndexedFolder>>
{
    /// <inheritdoc />
    /// <returns>The folders whose status changed.</returns>
    public async Task<IReadOnlyList<IndexedFolder>> Handle(CheckFolderAvailabilityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return [];
        }

        // Checked at once, so unreachable network shares time out together rather than one after another.
        var folders = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(folder => request.FolderIds is null || request.FolderIds.Contains(folder.Id))
            .Where(folder => !request.OfflineOnly || folder.Status == FolderStatus.Offline)
            .ToList();
        // A probe of an unreachable share may ignore the token until the SMB timeout, so the wait itself is cancellable.
        var available = await Task.WhenAll(folders.Select(folder => files.IsAvailableAsync(folder.ToHandle(), cancellationToken)))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = new List<IndexedFolder>();
        var labels = await Task.WhenAll(folders.Select((folder, i) => available[i]
            ? files.GetVolumeLabelAsync(folder.ToHandle(), cancellationToken)
            : Task.FromResult<string?>(null))).WaitAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var status = available[i] ? FolderStatus.Online : FolderStatus.Offline;
            var relabelled = available[i] && folder.ChangeVolumeLabel(labels[i]);
            if (folder.Status == status)
            {
                if (relabelled)
                {
                    await library.Folders.UpdateAvailabilityAsync(folder, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (available[i])
            {
                folder.MarkOnline(clock.GetUtcNow());
            }
            else
            {
                folder.MarkOffline(clock.GetUtcNow());
            }

            await library.Folders.UpdateAvailabilityAsync(folder, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Folder {Folder} is {Status} now", folder.DisplayPath, status);
            changed.Add(folder);
        }

        // The parked work of another library, opened since the check began, isn't this check's to release.
        if (changed.Any(folder => folder.Status == FolderStatus.Online)
            && jobs?.Current is { } scheduler
            && ReferenceEquals(scheduler.Library, library))
        {
            scheduler.ReleaseParked();
        }

        return changed;
    }
}
