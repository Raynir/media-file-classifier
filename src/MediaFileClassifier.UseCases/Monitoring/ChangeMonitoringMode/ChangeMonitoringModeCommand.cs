using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeMonitoringMode;

/// <summary>
/// Changes how a folder is kept up to date (E04-S05-T01). The watchers and polls follow by themselves, through the folder
/// repository's <c>Changed</c>. A folder that leaves Manual mode for one that runs on its own gets a quick sync (E04-S03)
/// now, to catch up with what changed while nothing ran.
/// </summary>
/// <param name="FolderId">The folder.</param>
/// <param name="Mode">Its new mode.</param>
public sealed record ChangeMonitoringModeCommand(Id<IndexedFolder> FolderId, MonitoringMode Mode) : IRequest<ChangeMonitoringModeResult>;

/// <summary>
/// What changing the mode did.
/// </summary>
public enum ChangeMonitoringModeResult
{
    /// <summary>The mode changed.</summary>
    Changed,

    /// <summary>The folder had that mode already.</summary>
    Unchanged,

    /// <summary>There's no library open, or no such folder.</summary>
    NotFound,
}

/// <summary>
/// Handles <see cref="ChangeMonitoringModeCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="locations">Tells the mode a folder runs in.</param>
/// <param name="sender">Starts the catch-up sync.</param>
/// <param name="logger">Logger.</param>
public sealed class ChangeMonitoringModeCommandHandler(
    ILibrarySession session,
    FolderLocations locations,
    ISender sender,
    ILogger<ChangeMonitoringModeCommandHandler> logger) : IRequestHandler<ChangeMonitoringModeCommand, ChangeMonitoringModeResult>
{
    /// <inheritdoc />
    public async Task<ChangeMonitoringModeResult> Handle(ChangeMonitoringModeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown monitoring mode.");
        }

        if (session.Current is not { } library
            || await library.Folders.GetAsync(request.FolderId, cancellationToken).ConfigureAwait(false) is not { } folder)
        {
            return ChangeMonitoringModeResult.NotFound;
        }

        var wasManual = folder.MonitoringMode == MonitoringMode.Manual;
        if (!folder.ChangeMonitoringMode(request.Mode))
        {
            return ChangeMonitoringModeResult.Unchanged;
        }

        await library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
        var effective = await locations.GetEffectiveModeAsync(folder, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Folder {Folder} is now in {Mode} mode, running as {Effective}", folder.DisplayPath, request.Mode, effective);

        if (wasManual && folder.Status == FolderStatus.Online)
        {
            var sync = await sender.Send(new StartRescanCommand(folder.Id, Quick: true), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Catch-up sync of {Folder} after Manual mode: {Outcome}", folder.DisplayPath, sync.Outcome);
        }

        return ChangeMonitoringModeResult.Changed;
    }
}
