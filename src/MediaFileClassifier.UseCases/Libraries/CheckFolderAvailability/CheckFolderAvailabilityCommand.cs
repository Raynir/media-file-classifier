using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Monitoring;
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
/// stored, so a relink or rule change made while the probes run isn't undone. The identity of an Online folder's volume
/// is stored too (E04-S04-T03): a folder whose drive letter now leads to another drive is Offline, and one whose drive
/// came back under another letter follows it there. Folders that came back are announced with
/// <see cref="FoldersReconnected"/> (E04-S04-T02).
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
/// <param name="publisher">Tells that folders came back Online (<see cref="FoldersReconnected"/>), or
/// <see langword="null"/>.</param>
/// <param name="volumes">Reads and finds the folders' volumes, or <see langword="null"/> where the platform has none.</param>
/// <param name="sender">Records a folder that followed its drive to another letter in the activity log, or
/// <see langword="null"/>.</param>
public sealed class CheckFolderAvailabilityCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<CheckFolderAvailabilityCommandHandler> logger,
    IJobSystem? jobs = null,
    IPublisher? publisher = null,
    IVolumeLocator? volumes = null,
    ISender? sender = null) : IRequestHandler<CheckFolderAvailabilityCommand, IReadOnlyList<IndexedFolder>>
{
    /// <inheritdoc />
    /// <returns>The folders whose status changed, and those that followed their drive to another letter.</returns>
    public async Task<IReadOnlyList<IndexedFolder>> Handle(CheckFolderAvailabilityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return [];
        }

        var all = await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var folders = all
            .Where(folder => request.FolderIds is null || request.FolderIds.Contains(folder.Id))
            .Where(folder => !request.OfflineOnly || folder.Status == FolderStatus.Offline)
            .ToList();

        // Checked at once, so unreachable network shares time out together rather than one after another. A probe of an
        // unreachable share may ignore the token until the SMB timeout, so the wait itself is cancellable.
        var probes = await Task.WhenAll(folders.Select(folder => ProbeAsync(folder, cancellationToken)))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = new List<IndexedFolder>();
        var reconnected = new List<Id<IndexedFolder>>();

        // Once the probes are done, what they found is stored and the parked work released even if the check is
        // cancelled now: a volume check that cancels this poll would find the folder already Online and release nothing.
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var probe = probes[i];
            if (probe.MovedTo is { } movedTo && await RelinkAsync(library, all, folder, movedTo, probe.Label).ConfigureAwait(false))
            {
                changed.Add(folder);
                reconnected.Add(folder.Id);
                continue;
            }

            var status = probe.Available ? FolderStatus.Online : FolderStatus.Offline;

            // A label or identity that couldn't be read keeps the stored one, so the drive stays named while it's Offline.
            var relabelled = probe.Label.Read && folder.ChangeVolumeLabel(probe.Label.Label);
            relabelled |= probe.VolumeId is { } volumeId && folder.ChangeVolumeId(volumeId);
            if (folder.Status == status)
            {
                if (relabelled)
                {
                    await library.Folders.UpdateAvailabilityAsync(folder, CancellationToken.None).ConfigureAwait(false);
                }

                continue;
            }

            if (probe.Available)
            {
                folder.MarkOnline(clock.GetUtcNow());
                reconnected.Add(folder.Id);
            }
            else
            {
                folder.MarkOffline(clock.GetUtcNow());
            }

            await library.Folders.UpdateAvailabilityAsync(folder, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Folder {Folder} is {Status} now", folder.DisplayPath, status);
            changed.Add(folder);
        }

        // The parked work of another library, opened since the check began, isn't this check's to release.
        if (reconnected.Count > 0
            && jobs?.Current is { } scheduler
            && ReferenceEquals(scheduler.Library, library))
        {
            scheduler.ReleaseParked();
        }

        // Told after the statuses are stored, so a catch-up finds the folders Online. A folder of a library closed
        // meanwhile isn't caught up: it's checked again when its library opens.
        if (publisher is not null && reconnected.Count > 0 && ReferenceEquals(session.Current, library))
        {
            try
            {
                await publisher.Publish(new FoldersReconnected(reconnected), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The statuses are stored; a catch-up that couldn't start leaves the changes to the next sync or rescan.
                logger.LogWarning(exception, "Couldn't start the catch-up of {Count} reconnected folder(s)", reconnected.Count);
            }
        }

        return changed;
    }

    /// <summary>
    /// Finds whether a folder can be reached on its own volume, its volume's label and identity, and, when it can't be
    /// reached, whether its volume is mounted under another root.
    /// </summary>
    private async Task<Probe> ProbeAsync(IndexedFolder folder, CancellationToken cancellationToken)
    {
        var handle = folder.ToHandle();
        var available = await files.IsAvailableAsync(handle, cancellationToken).ConfigureAwait(false);
        string? volumeId = null;
        if (available && volumes is not null)
        {
            volumeId = await FolderReachability.ReadVolumeIdAsync(volumes, folder, cancellationToken).ConfigureAwait(false);
            if (volumeId is not null && folder.VolumeId is { } expected && !string.Equals(volumeId, expected, StringComparison.OrdinalIgnoreCase))
            {
                // Another drive took the folder's letter: it isn't the folder, whatever is on it.
                logger.LogWarning("{Folder} leads to another drive ({Found}, not {Expected}), so it's Offline", folder.DisplayPath, volumeId, expected);
                available = false;
                volumeId = null;
            }
        }

        if (available)
        {
            return new Probe(true, await VolumeLabels.TryReadAsync(files, handle, logger, cancellationToken).ConfigureAwait(false), volumeId, null);
        }

        if (volumes is null || folder.VolumeId is not { } stored)
        {
            return new Probe(false, VolumeLabels.Unknown, null, null);
        }

        FolderHandle? movedTo;
        try
        {
            movedTo = await volumes.FindOnVolumeAsync(handle, stored, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't look for the drive of {Folder} under other letters", folder.DisplayPath);
            movedTo = null;
        }

        var label = movedTo is null ? VolumeLabels.Unknown : await VolumeLabels.TryReadAsync(files, movedTo, logger, cancellationToken).ConfigureAwait(false);
        return new Probe(false, label, null, movedTo);
    }

    /// <summary>
    /// Points a folder at the root its volume is mounted under now, unless that root is another folder of the library or
    /// overlaps one, so two folders never index the same files.
    /// </summary>
    /// <returns>Whether the folder was relinked.</returns>
    private async Task<bool> RelinkAsync(IOpenLibrary library, IReadOnlyList<IndexedFolder> all, IndexedFolder folder, FolderHandle movedTo, (bool Read, string? Label) label)
    {
        var overlapping = all.FirstOrDefault(other => other.Id != folder.Id
            && (string.Equals(other.PlatformHandle, movedTo.Id, StringComparison.OrdinalIgnoreCase)
                || files.GetPathInside(movedTo, other.ToHandle()) is not null
                || files.GetPathInside(other.ToHandle(), movedTo) is not null));
        if (overlapping is not null)
        {
            logger.LogWarning("The drive of {Folder} is back at {NewPath}, which overlaps {Other}, so the folder stays where it was", folder.DisplayPath, movedTo.DisplayName, overlapping.DisplayPath);
            return false;
        }

        var oldPath = folder.DisplayPath;
        var oldHandle = folder.PlatformHandle;
        folder.Relink(movedTo.DisplayName, movedTo.Id, clock.GetUtcNow());
        if (label.Read)
        {
            folder.ChangeVolumeLabel(label.Label);
        }

        if (!await library.Folders.RelinkAsync(folder, oldHandle, CancellationToken.None).ConfigureAwait(false))
        {
            // Relinked or removed meanwhile: the newer state wins.
            return false;
        }

        logger.LogInformation("Folder {OldPath} followed its drive to {NewPath}", oldPath, folder.DisplayPath);
        if (sender is not null)
        {
            await sender.Send(
                new RecordActivityCommand(ActivityEventType.FolderRelinked, Subject: $"{oldPath} → {folder.DisplayPath}", FolderId: folder.Id),
                CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// What the probe of one folder found.
    /// </summary>
    /// <param name="Available">Whether the folder can be reached on its own volume.</param>
    /// <param name="Label">Its volume's label, if read.</param>
    /// <param name="VolumeId">Its volume's identity, if read while reachable.</param>
    /// <param name="MovedTo">Its root on its volume under another drive letter, if found.</param>
    private sealed record Probe(bool Available, (bool Read, string? Label) Label, string? VolumeId, FolderHandle? MovedTo);
}
