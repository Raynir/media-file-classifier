using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.UseCases.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Tells where each folder is (network share, cloud-synced) and so which monitoring mode it runs in (E04-S05). A folder
/// in <see cref="MonitoringMode.Auto"/> is polled on a network share and watched elsewhere. The location of an Online
/// folder is asked once per root and kept; an Offline folder's is asked each time, because a disconnected mapped drive
/// may not say it's remote.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="probe">Finds where a folder is; without one (a composition with no platform layer), every folder is
/// local.</param>
public sealed class FolderLocations(ILogger<FolderLocations> logger, IFolderLocationProbe? probe = null)
{
    private readonly ConcurrentDictionary<string, FolderLocation> known = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the mode a folder runs in: its own mode, or for <see cref="MonitoringMode.Auto"/> Poll on a network share
    /// and Watch elsewhere.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="location">Where it is.</param>
    /// <returns>Watch, Poll or Manual.</returns>
    public static MonitoringMode EffectiveMode(IndexedFolder folder, FolderLocation location)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(location);

        return folder.MonitoringMode == MonitoringMode.Auto
            ? location.IsNetwork ? MonitoringMode.Poll : MonitoringMode.Watch
            : folder.MonitoringMode;
    }

    /// <summary>
    /// Finds where a folder is. When the platform can't tell, or asking fails, it's local.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where it is.</returns>
    public async Task<FolderLocation> GetAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (known.TryGetValue(folder.PlatformHandle, out var location))
        {
            return location;
        }

        if (probe is null)
        {
            return FolderLocation.Local;
        }

        try
        {
            location = await probe.ProbeAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Couldn't tell where {Folder} is; taking it as local", folder.DisplayPath);
            return FolderLocation.Local;
        }

        if (folder.Status == FolderStatus.Online)
        {
            known[folder.PlatformHandle] = location;
        }

        return location;
    }

    /// <summary>
    /// Gets the mode a folder runs in, as <see cref="EffectiveMode"/> does. A folder whose own mode isn't Auto is
    /// answered without asking where it is.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Watch, Poll or Manual.</returns>
    public async Task<MonitoringMode> GetEffectiveModeAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return folder.MonitoringMode == MonitoringMode.Auto
            ? EffectiveMode(folder, await GetAsync(folder, cancellationToken).ConfigureAwait(false))
            : folder.MonitoringMode;
    }
}
