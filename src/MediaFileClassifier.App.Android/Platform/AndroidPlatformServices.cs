using Android.Content;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using AndroidUri = Android.Net.Uri;

namespace MediaFileClassifier.App.Android.Platform;

/// <summary>
/// Android implementation of the platform services: the Storage Access Framework pickers through Avalonia.
/// A picked folder's <see cref="FolderHandle.Id"/> is its persisted tree URI.
/// </summary>
/// <param name="paths">Per-user app folders.</param>
/// <param name="contentResolver">The app's content resolver.</param>
internal sealed class AndroidPlatformServices(IAppPaths paths, ContentResolver contentResolver) : IPlatformServices
{
    /// <inheritdoc />
    public IAppPaths Paths { get; } = paths;

    /// <inheritdoc />
    public bool CanRevealInFileExplorer => false;

    /// <inheritdoc />
    public async Task<FolderHandle?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await Dispatcher.UIThread.InvokeAsync(
            () => GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title }));
        if (folders.Count == 0)
        {
            return null;
        }

        // Keep access across restarts. Android limits how many grants an app can persist.
        var treeUri = AndroidUri.Parse(folders[0].Path.AbsoluteUri)!;
        contentResolver.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission);
        return new FolderHandle(treeUri.ToString()!, folders[0].Name);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileLocation>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
        };
        var files = await Dispatcher.UIThread.InvokeAsync(() => GetStorageProvider().OpenFilePickerAsync(options));

        // A picked file has no granted parent folder, so the file's URI stands in for the folder.
        return files
            .Select(file => new FileLocation(new FolderHandle(file.Path.AbsoluteUri, file.Name), string.Empty))
            .ToList();
    }

    /// <inheritdoc />
    public Task RevealInFileExplorerAsync(FileLocation location, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Android has no file manager to reveal a file in.");

    /// <inheritdoc />
    public Task OpenWithDefaultAppAsync(FileLocation location, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not part of the smoke test (MFC-18). E20 adds an ACTION_VIEW intent.");

    private static IStorageProvider GetStorageProvider()
        => Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } view }
            && TopLevel.GetTopLevel(view) is { } topLevel
            ? topLevel.StorageProvider
            : throw new InvalidOperationException("The pickers need the main view to be shown.");
}
