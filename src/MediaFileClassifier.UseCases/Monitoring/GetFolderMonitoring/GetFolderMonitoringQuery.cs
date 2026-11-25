using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring.GetFolderMonitoring;

/// <summary>
/// Lists how each folder of the open library is kept up to date (E04-S05): its mode, the mode it runs in, its state, and
/// where it is (network share, cloud-synced). Folders being removed aren't listed. Read it again on
/// <see cref="FolderMonitoringChanged"/>.
/// </summary>
public sealed record GetFolderMonitoringQuery : IRequest<IReadOnlyList<FolderMonitoringInfo>>;

/// <summary>
/// How a folder is kept up to date.
/// </summary>
/// <param name="FolderId">The folder.</param>
/// <param name="Mode">Its own mode, as the user set it.</param>
/// <param name="EffectiveMode">The mode it runs in: Watch, Poll or Manual (Auto picks Poll on a network share).</param>
/// <param name="State">What keeps it up to date now.</param>
/// <param name="IsNetwork">Whether it's on a network share, where Poll is suggested.</param>
/// <param name="CloudProvider">The cloud client that syncs it, e.g. <c>OneDrive</c>, or <see langword="null"/>.</param>
/// <param name="NextPoll">When it's polled next, while it's polled.</param>
public sealed record FolderMonitoringInfo(
    Id<IndexedFolder> FolderId,
    MonitoringMode Mode,
    MonitoringMode EffectiveMode,
    FolderMonitoringState State,
    bool IsNetwork,
    string? CloudProvider,
    DateTimeOffset? NextPoll);

/// <summary>
/// Handles <see cref="GetFolderMonitoringQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="locations">Tells where each folder is.</param>
/// <param name="watchers">Tells which folders are watched.</param>
/// <param name="poller">Tells which folders are polled.</param>
/// <param name="settings">Tells whether monitoring is on for the library.</param>
/// <param name="pause">Tells whether monitoring is paused, or <see langword="null"/> for no pause.</param>
public sealed class GetFolderMonitoringQueryHandler(
    ILibrarySession session,
    FolderLocations locations,
    FolderWatchManager watchers,
    FolderPoller poller,
    ISettingsStore settings,
    MonitoringPause? pause = null) : IRequestHandler<GetFolderMonitoringQuery, IReadOnlyList<FolderMonitoringInfo>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FolderMonitoringInfo>> Handle(GetFolderMonitoringQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        var watched = watchers.WatchedFolders.ToHashSet();
        var enabled = settings.Get(KnownSettings.MonitoringEnabled);
        var paused = pause?.IsPaused == true;
        var result = new List<FolderMonitoringInfo>();
        foreach (var folder in await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false))
        {
            var location = await locations.GetAsync(folder, cancellationToken).ConfigureAwait(false);
            var effective = FolderLocations.EffectiveMode(folder, location);
            var state = (folder.Status, effective) switch
            {
                (FolderStatus.Offline, _) => FolderMonitoringState.Offline,
                _ when !enabled => FolderMonitoringState.Off,
                (_, MonitoringMode.Watch) when !watchers.IsWatching => FolderMonitoringState.Manual,
                (_, MonitoringMode.Watch) when watched.Contains(folder.Id) => paused ? FolderMonitoringState.Paused : FolderMonitoringState.Watching,
                (_, MonitoringMode.Watch) => FolderMonitoringState.Error,
                (_, MonitoringMode.Poll) when poller.IsPolling => paused ? FolderMonitoringState.Paused : FolderMonitoringState.Polling,
                _ => FolderMonitoringState.Manual,
            };
            result.Add(new FolderMonitoringInfo(
                folder.Id,
                folder.MonitoringMode,
                effective,
                state,
                location.IsNetwork,
                location.CloudProvider,
                state is FolderMonitoringState.Polling or FolderMonitoringState.Paused ? poller.NextPollOf(folder.Id) : null));
        }

        return result;
    }
}
