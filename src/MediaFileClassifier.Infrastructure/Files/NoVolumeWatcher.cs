using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// The default <see cref="IVolumeWatcher"/> for platforms without volume events (E14-S07-T04): it never reports a
/// change. Where the head starts <c>FolderAvailabilityMonitor</c> (Desktop does), folders come back through its poll of
/// Offline folders alone; where it doesn't (the Android head, for now), statuses change only through the checks when a
/// library opens, before imports, when the folders view opens and on a rescan.
/// </summary>
internal sealed class NoVolumeWatcher : IVolumeWatcher
{
    /// <inheritdoc />
    public event EventHandler? VolumesChanged
    {
        add { }
        remove { }
    }
}
