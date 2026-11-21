namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Tells when a volume (a drive, a memory card, a mounted share) arrived or went away (E14-S07-T04, DNEW-4), so the
/// indexed folders can be checked again at once instead of waiting for the next poll. Windows implements it with
/// volume device events; a platform without such events uses the no-op default, which never raises
/// <see cref="VolumesChanged"/>.
/// </summary>
public interface IVolumeWatcher
{
    /// <summary>
    /// Raised when a volume arrived or was removed. It can be raised on any thread, and several times for one change.
    /// </summary>
    event EventHandler? VolumesChanged;
}
