using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MediaFileClassifier.Ui.Platform;

/// <summary>
/// Opens a folder in the system file manager.
/// </summary>
public interface IFolderLauncher
{
    /// <summary>
    /// Opens a folder, creating it first if it doesn't exist.
    /// </summary>
    /// <param name="path">Folder path.</param>
    /// <returns><c>true</c> if the file manager was started.</returns>
    Task<bool> OpenFolderAsync(string path);
}

/// <summary>
/// Opens folders through Avalonia's launcher, which works on every platform head.
/// </summary>
public sealed class AvaloniaFolderLauncher : IFolderLauncher
{
    /// <inheritdoc />
    public async Task<bool> OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        var topLevel = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null,
        };
        return topLevel is not null && await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
