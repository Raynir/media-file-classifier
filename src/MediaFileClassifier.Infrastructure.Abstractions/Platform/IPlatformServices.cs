using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;

namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Platform-specific services: app folders, pickers and the system shell (E01-S07). Each platform head
/// registers its own implementation, so the shared layers make no platform API calls.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Gets the per-user app-data, cache and log folders.
    /// </summary>
    IAppPaths Paths { get; }

    /// <summary>
    /// Gets a value indicating whether the platform has a file manager that
    /// <see cref="RevealInFileExplorerAsync"/> can open.
    /// </summary>
    bool CanRevealInFileExplorer { get; }

    /// <summary>
    /// Asks the user to pick a folder, and grants the app access to it.
    /// </summary>
    /// <param name="title">Title of the picker dialog.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The picked folder, or <see langword="null"/> when the user cancelled.</returns>
    Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the user to pick one or more files.
    /// </summary>
    /// <param name="request">What to ask for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The picked files, or an empty list when the user cancelled.</returns>
    Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the platform's file manager with the file selected, or the folder open when
    /// <see cref="FileLocation.RelativePath"/> is empty.
    /// </summary>
    /// <param name="location">File or folder to show.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the file manager was started.</returns>
    /// <exception cref="NotSupportedException">The platform has no file manager; see <see cref="CanRevealInFileExplorer"/>.</exception>
    Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the file with the app the user set as its default.
    /// </summary>
    /// <param name="location">File to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the app was started.</returns>
    Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default);
}
