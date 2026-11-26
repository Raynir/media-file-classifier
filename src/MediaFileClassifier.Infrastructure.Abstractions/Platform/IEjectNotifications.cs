using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Tells when the user asks to eject the drive a folder is on (E04-S04-T01), so whatever holds the folder open, e.g. its
/// watcher, lets go and the eject isn't refused as "in use". Windows asks only the apps that registered an open handle
/// for it, so a folder must be registered while it's held. Platforms without such requests don't implement it.
/// </summary>
public interface IEjectNotifications
{
    /// <summary>
    /// Starts listening for eject requests of the drive a folder is on.
    /// </summary>
    /// <param name="folder">The folder, which should be reachable.</param>
    /// <returns>The registration, which the caller disposes, or <see langword="null"/> when the folder's drive can't
    /// be ejected or can't be listened to, e.g. a network share.</returns>
    IEjectRegistration? Register(FolderHandle folder);
}
