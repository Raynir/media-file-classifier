using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Testing.Platform;

/// <summary>
/// Eject requests the test raises (E04-S04): each registered folder gets a <see cref="FakeEjectRegistration"/> whose
/// request and cancellation the test fires, as the window's message hook would.
/// </summary>
public sealed class FakeEjectNotifications : IEjectNotifications
{
    /// <summary>
    /// Gets every registration made, disposed ones included.
    /// </summary>
    public List<FakeEjectRegistration> Registrations { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether folders can't be registered, as a network share can't.
    /// </summary>
    public bool Unsupported { get; set; }

    /// <inheritdoc />
    public IEjectRegistration? Register(FolderHandle folder)
    {
        if (Unsupported)
        {
            return null;
        }

        var registration = new FakeEjectRegistration(folder);
        Registrations.Add(registration);
        return registration;
    }
}

/// <summary>
/// One folder registered with <see cref="FakeEjectNotifications"/>.
/// </summary>
/// <param name="folder">The folder.</param>
public sealed class FakeEjectRegistration(FolderHandle folder) : IEjectRegistration
{
    /// <inheritdoc />
    public event EventHandler? EjectRequested;

    /// <inheritdoc />
    public event EventHandler? EjectCancelled;

    /// <summary>
    /// Gets the folder.
    /// </summary>
    public FolderHandle Folder { get; } = folder;

    /// <summary>
    /// Gets a value indicating whether the registration was disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Raises <see cref="EjectRequested"/>, as Windows does when the user asks to eject the drive.
    /// </summary>
    public void RequestEject() => EjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises <see cref="EjectCancelled"/>, as Windows does when the eject is refused.
    /// </summary>
    public void CancelEject() => EjectCancelled?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}
