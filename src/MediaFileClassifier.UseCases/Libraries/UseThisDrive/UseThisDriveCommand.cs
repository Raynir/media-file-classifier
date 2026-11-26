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

namespace MediaFileClassifier.UseCases.Libraries.UseThisDrive;

/// <summary>
/// Accepts the drive now at an Offline folder's root as the folder's drive (MFC-1051), e.g. after the drive was cloned to
/// a new disk or reformatted and restored, which gives it a new identity. Every other Offline folder from the same old
/// drive whose root leads to the same new drive takes it too. For each, the new identity and label are stored and the
/// folder is Online with its items kept, and the activity log records it; then parked work is released, and
/// <see cref="FoldersReconnected"/> starts the usual catch-up, a quick sync that marks files not on the drive Missing.
/// The drives are read again here, so a drive swapped since the dialog asked <see cref="FindOtherDriveQuery"/> isn't
/// accepted unseen, and a folder relinked or removed meanwhile is left as the newer write stored it.
/// </summary>
/// <param name="FolderId">The Offline folder.</param>
public sealed record UseThisDriveCommand(Id<IndexedFolder> FolderId) : IRequest<UseThisDriveResult>;

/// <summary>
/// What became of <see cref="UseThisDriveCommand"/>.
/// </summary>
/// <param name="Outcome">Whether the drive was accepted.</param>
/// <param name="OtherFolders">How many other folders took the drive with the requested one.</param>
public sealed record UseThisDriveResult(UseThisDriveOutcome Outcome, int OtherFolders = 0);

/// <summary>
/// Whether <see cref="UseThisDriveCommand"/> accepted the drive.
/// </summary>
public enum UseThisDriveOutcome
{
    /// <summary>The folder is on the drive at its root now, and Online.</summary>
    Done,

    /// <summary>The folder's root doesn't lead to another drive (any more), so nothing changed.</summary>
    NoOtherDrive,

    /// <summary>No library is open, or the folder was removed or relinked meanwhile.</summary>
    NotFound,
}

/// <summary>
/// Handles <see cref="UseThisDriveCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Lists the folders' roots and reads the label.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="volumes">Reads the drives' identities, or <see langword="null"/> where the platform has none.</param>
/// <param name="jobs">Releases the parked work, or <see langword="null"/>.</param>
/// <param name="publisher">Tells that the folders came back Online, or <see langword="null"/>.</param>
/// <param name="sender">Records the change in the activity log, or <see langword="null"/>.</param>
public sealed class UseThisDriveCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<UseThisDriveCommandHandler> logger,
    IVolumeLocator? volumes = null,
    IJobSystem? jobs = null,
    IPublisher? publisher = null,
    ISender? sender = null) : IRequestHandler<UseThisDriveCommand, UseThisDriveResult>
{
    /// <inheritdoc />
    public async Task<UseThisDriveResult> Handle(UseThisDriveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return new UseThisDriveResult(UseThisDriveOutcome.NotFound);
        }

        var all = await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(folder => folder.Id == request.FolderId) is not { } requested)
        {
            return new UseThisDriveResult(UseThisDriveOutcome.NotFound);
        }

        if (await OtherDrive.FindWithOthersAsync(files, volumes, requested, all, logger, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            return new UseThisDriveResult(UseThisDriveOutcome.NoOtherDrive);
        }

        var (drive, others) = found;

        // From here on changes are stored, so they're announced even if the caller cancels.
        var accepted = new List<Id<IndexedFolder>>();
        foreach (var folder in others.Prepend(requested))
        {
            if (await AcceptAsync(library, folder, drive).ConfigureAwait(false))
            {
                accepted.Add(folder.Id);
            }
        }

        if (accepted.Count == 0 || accepted[0] != requested.Id)
        {
            // The requested folder was relinked or removed meanwhile; any other folder that took the drive is announced.
            await AnnounceAsync(library, accepted).ConfigureAwait(false);
            return new UseThisDriveResult(UseThisDriveOutcome.NotFound);
        }

        await AnnounceAsync(library, accepted).ConfigureAwait(false);
        return new UseThisDriveResult(UseThisDriveOutcome.Done, accepted.Count - 1);
    }

    /// <summary>
    /// Stores the drive as a folder's drive and brings it Online.
    /// </summary>
    /// <returns><see langword="false"/> when the folder was relinked or removed since it was read, so nothing was
    /// stored.</returns>
    private async Task<bool> AcceptAsync(IOpenLibrary library, IndexedFolder folder, OtherDrive drive)
    {
        var oldId = folder.VolumeId;
        folder.ChangeVolumeId(drive.VolumeId);
        if (drive.Label.Read)
        {
            folder.ChangeVolumeLabel(drive.Label.Label);
        }

        folder.MarkOnline(clock.GetUtcNow());

        // A relink to the same root stores the availability and volume only if the folder still has that root, so a
        // relink by a parallel check (MFC-1044) or a removal wins, and says whether it did.
        if (!await library.Folders.RelinkAsync(folder, folder.PlatformHandle, CancellationToken.None).ConfigureAwait(false))
        {
            logger.LogInformation("Folder {Folder} was relinked or removed meanwhile, so it keeps its drive", folder.DisplayPath);
            return false;
        }

        logger.LogInformation("Folder {Folder} uses the drive at its root now ({OldId} → {NewId})", folder.DisplayPath, oldId, drive.VolumeId);
        if (sender is not null)
        {
            await sender.Send(
                new RecordActivityCommand(ActivityEventType.FolderDriveReplaced, Subject: folder.DisplayPath, FolderId: folder.Id),
                CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Releases the parked work and starts the catch-up of the folders that took the drive.
    /// </summary>
    private async Task AnnounceAsync(IOpenLibrary library, IReadOnlyList<Id<IndexedFolder>> accepted)
    {
        if (accepted.Count == 0)
        {
            return;
        }

        if (jobs?.Current is { } scheduler && ReferenceEquals(scheduler.Library, library))
        {
            scheduler.ReleaseParked();
        }

        if (publisher is not null && ReferenceEquals(session.Current, library))
        {
            try
            {
                await publisher.Publish(new FoldersReconnected(accepted), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The folders are Online; a catch-up that couldn't start leaves the changes to the next sync or rescan.
                logger.LogWarning(exception, "Couldn't start the catch-up of {Count} folder(s) on their new drive", accepted.Count);
            }
        }
    }
}
