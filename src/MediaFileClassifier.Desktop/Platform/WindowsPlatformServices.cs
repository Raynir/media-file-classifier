using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Windows implementation of the platform services (D028): Avalonia pickers on the main window, and Explorer.
/// </summary>
/// <param name="paths">Per-user app folders.</param>
internal sealed class WindowsPlatformServices(IAppPaths paths) : IPlatformServices
{
    /// <inheritdoc />
    public IAppPaths Paths { get; } = paths;

    /// <inheritdoc />
    public bool CanRevealInFileExplorer => true;

    /// <inheritdoc />
    public async Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await Dispatcher.UIThread.InvokeAsync(
            () => GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title }));
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return path is null ? null : LocalFolder.ToHandle(path);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = request.FileTypes.Count == 0
                ? null
                : request.FileTypes.Select(type => new FilePickerFileType(type.Name) { Patterns = type.Patterns }).ToList(),
        };
        var files = await Dispatcher.UIThread.InvokeAsync(
            () => GetStorageProvider().OpenFilePickerAsync(options));
        return files
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .Select(LocalFolder.ToLocation)
            .ToList();
    }

    /// <inheritdoc />
    public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartAndForget(new ProcessStartInfo("explorer.exe", GetExplorerArguments(location)));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = LocalFolder.GetFullPath(location);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("The file to open doesn't exist.", path);
        }

        StartAndForget(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the Explorer command line: <c>/select,"file"</c> for a file, or the quoted folder path.
    /// </summary>
    /// <param name="location">File or folder to show.</param>
    /// <returns>Arguments for <c>explorer.exe</c>.</returns>
    internal static string GetExplorerArguments(FileLocation location)
    {
        var path = LocalFolder.GetFullPath(location);
        return string.IsNullOrEmpty(location.RelativePath) ? $"\"{path}\"" : $"/select,\"{path}\"";
    }

    private static IStorageProvider GetStorageProvider()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.StorageProvider
            : throw new InvalidOperationException("The pickers need the main window to be open.");

    private static void StartAndForget(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
    }
}
