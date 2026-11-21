using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Import.ImportFiles;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// A file or folder dropped on the main window, as a path.
/// </summary>
/// <param name="Path">The full path.</param>
/// <param name="IsFolder">Whether it's a folder.</param>
public readonly record struct DroppedPath(string Path, bool IsFolder);

/// <summary>
/// Drag-and-drop import (E03-S06): the drop overlay's state, and what a drop does. Dropped files are imported as picked
/// files are (E03-S05), dropped folders go through the indexed folders dialog like the Add folder flow (E02-S03), and
/// one toast sums both up.
/// </summary>
/// <param name="importFiles">Imports files without saying anything.</param>
/// <param name="addFolders">Adds folders through the folders dialog and returns how many were added.</param>
/// <param name="notifications">Shows the summary.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library: nothing can be dropped without one, or <c>null</c> for none checked.</param>
public sealed partial class DropImportViewModel(
    Func<IReadOnlyList<FileLocation>, Task<ImportFilesResult>> importFiles,
    Func<IReadOnlyList<FolderHandle>, Task<int>> addFolders,
    INotificationService notifications,
    Localizer localizer,
    ILibrarySession? session = null) : ViewModelBase
{
    /// <summary>
    /// Gets or sets a value indicating whether items are being dragged over the window, so the overlay shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDragActive { get; set; }

    /// <summary>
    /// Gets a value indicating whether a drop can be accepted: it needs an open library.
    /// </summary>
    public bool CanAccept => session is null || session.Current is not null;

    /// <summary>
    /// Splits dropped paths into the folders to add and the files to import. Repeated paths count once.
    /// </summary>
    /// <param name="paths">What was dropped.</param>
    /// <returns>The folders and the files.</returns>
    public static (IReadOnlyList<FolderHandle> Folders, IReadOnlyList<FileLocation> Files) Resolve(IEnumerable<DroppedPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var folders = new List<FolderHandle>();
        var files = new List<FileLocation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dropped in paths)
        {
            var full = Path.GetFullPath(dropped.Path);
            if (!seen.Add(Path.TrimEndingDirectorySeparator(full)))
            {
                continue;
            }

            if (dropped.IsFolder)
            {
                folders.Add(ToHandle(full));
            }
            else if (Path.GetDirectoryName(full) is { } parent)
            {
                files.Add(new FileLocation(ToHandle(parent), Path.GetFileName(full)));
            }
        }

        return (folders, files);
    }

    /// <summary>
    /// Imports what was dropped: the files first, then the folders (which may ask questions), then one summary.
    /// </summary>
    /// <param name="paths">What was dropped.</param>
    /// <returns>A task that completes when the summary is shown.</returns>
    public async Task DropAsync(IEnumerable<DroppedPath> paths)
    {
        IsDragActive = false;
        if (!CanAccept)
        {
            return;
        }

        var (folders, files) = Resolve(paths);
        if (folders.Count == 0 && files.Count == 0)
        {
            return;
        }

        var result = files.Count > 0 ? await importFiles(files) : new ImportFilesResult();
        var foldersAdded = folders.Count > 0 ? await addFolders(folders) : 0;
        Summarize(result, foldersAdded);
    }

    private static FolderHandle ToHandle(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var name = Path.GetFileName(full);
        return new FolderHandle(full, string.IsNullOrEmpty(name) ? full : name);
    }

    private void Summarize(ImportFilesResult result, int foldersAdded)
    {
        var text = FileImporter.Describe(localizer, result);
        if (foldersAdded > 0)
        {
            var line = foldersAdded == 1 ? localizer.Get("DropImport_FoldersOne") : localizer.Format("DropImport_Folders", foldersAdded);
            text = text.Length == 0 ? line : text + Environment.NewLine + line;
        }

        if (text.Length == 0)
        {
            return;
        }

        var worked = result.Imported.Count > 0 || foldersAdded > 0;
        var severity = worked ? NotificationSeverity.Success
            : result.Unsupported.Count + result.Unreadable.Count > 0 ? NotificationSeverity.Warning
            : NotificationSeverity.Info;
        notifications.Notify(new Notification(text, severity));
    }
}
