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
/// a new disk or reformatted and restored, which gives it a new identity. The new identity and label are stored and the
/// folder is Online with its items kept; the activity log records it, parked work is released, and
/// <see cref="FoldersReconnected"/> starts the usual catch-up, a quick sync that marks files not on the drive Missing.
/// The drive is read again here, so a drive swapped since the dialog asked <see cref="FindOtherDriveQuery"/> isn't
/// accepted unseen.
/// </summary>
/// <param name="FolderId">The Offline folder.</param>
public sealed record UseThisDriveCommand(Id<IndexedFolder> FolderId) : IRequest<UseThisDriveOutcome>;

/// <summary>
/// What became of <see cref="UseThisDriveCommand"/>.
/// </summary>
public enum UseThisDriveOutcome
{
    /// <summary>The folder is on the drive at its root now, and Online.</summary>
    Done,

    /// <summary>The folder's root doesn't lead to another drive (any more), so nothing changed.</summary>
    NoOtherDrive,

    /// <summary>No library is open, or the folder was removed.</summary>
    NotFound,
}

/// <summary>
/// Handles <see cref="UseThisDriveCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Lists the folder's root and reads its label.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="volumes">Reads the drive's identity, or <see langword="null"/> where the platform has none.</param>
/// <param name="jobs">Releases the parked work, or <see langword="null"/>.</param>
/// <param name="publisher">Tells that the folder came back Online, or <see langword="null"/>.</param>
/// <param name="sender">Records the change in the activity log, or <see langword="null"/>.</param>
public sealed class UseThisDriveCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<UseThisDriveCommandHandler> logger,
    IVolumeLocator? volumes = null,
    IJobSystem? jobs = null,
    IPublisher? publisher = null,
    ISender? sender = null) : IRequestHandler<UseThisDriveCommand, UseThisDriveOutcome>
{
    /// <inheritdoc />
    public async Task<UseThisDriveOutcome> Handle(UseThisDriveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library
            || await library.Folders.GetAsync(request.FolderId, cancellationToken).ConfigureAwait(false) is not { } folder)
        {
            return UseThisDriveOutcome.NotFound;
        }

        if (await OtherDrive.FindAsync(files, volumes, folder, logger, cancellationToken).ConfigureAwait(false) is not { } drive)
        {
            return UseThisDriveOutcome.NoOtherDrive;
        }

        var oldId = folder.VolumeId;
        folder.ChangeVolumeId(drive.VolumeId);
        if (drive.Label.Read)
        {
            folder.ChangeVolumeLabel(drive.Label.Label);
        }

        folder.MarkOnline(clock.GetUtcNow());

        // Only the availability and volume columns, and only if the folder still has the root it was read with, so a
        // relink or rule change made meanwhile isn't undone. From here on the change is stored, so it's announced even if
        // the caller cancels.
        await library.Folders.UpdateAvailabilityAndVolumeAsync(folder, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Folder {Folder} uses the drive at its root now ({OldId} → {NewId})", folder.DisplayPath, oldId, drive.VolumeId);

        if (sender is not null)
        {
            await sender.Send(
                new RecordActivityCommand(ActivityEventType.FolderDriveReplaced, Subject: folder.DisplayPath, FolderId: folder.Id),
                CancellationToken.None).ConfigureAwait(false);
        }

        if (jobs?.Current is { } scheduler && ReferenceEquals(scheduler.Library, library))
        {
            scheduler.ReleaseParked();
        }

        if (publisher is not null && ReferenceEquals(session.Current, library))
        {
            try
            {
                await publisher.Publish(new FoldersReconnected([folder.Id]), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The folder is Online; a catch-up that couldn't start leaves the changes to the next sync or rescan.
                logger.LogWarning(exception, "Couldn't start the catch-up of {Folder}", folder.DisplayPath);
            }
        }

        return UseThisDriveOutcome.Done;
    }
}
