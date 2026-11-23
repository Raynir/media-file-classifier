namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Listens for eject requests of one folder's drive, until disposed.
/// </summary>
public interface IEjectRegistration : IDisposable
{
    /// <summary>
    /// Raised when the drive is about to be ejected. Handlers run synchronously and must release everything they hold
    /// open on the folder before they return, since the eject is refused while anything is open.
    /// </summary>
    event EventHandler? EjectRequested;

    /// <summary>
    /// Raised when an eject that was requested didn't happen, e.g. another app refused it, so the folder can be held
    /// again. A drive that was ejected raises nothing more; its volume going away is reported by
    /// <see cref="IVolumeWatcher"/>.
    /// </summary>
    event EventHandler? EjectCancelled;
}
