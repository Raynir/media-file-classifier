using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// The default <see cref="IVolumeWatcher"/> for platforms without volume events (E14-S07-T04): it never reports a
/// change, so folders come back through the poll of Offline folders alone.
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
