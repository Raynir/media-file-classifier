using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Ui.ViewModels.Navigation;

namespace MediaFileClassifier.Ui.Views.Navigation;

/// <summary>
/// The navigation panel: the sections as a tree when expanded, the icon rail when collapsed. Folders dropped on it are
/// added through the Folders section (E13-S02).
/// </summary>
public partial class NavigationPanelView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationPanelView"/> class.
    /// </summary>
    public NavigationPanelView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static List<FolderHandle> DroppedFolders(DragEventArgs e) =>
        [.. (e.DataTransfer.TryGetFiles() ?? [])
            .OfType<IStorageFolder>()
            .Select(folder => folder.TryGetLocalPath() is { } path ? new FolderHandle(path, folder.Name) : null)
            .OfType<FolderHandle>()];

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is NavigationPanelViewModel viewModel && DroppedFolders(e) is { Count: > 0 } folders)
        {
            // After the drop completes: a modal dialog inside the drop callback would block the app the folders came from.
            e.Handled = true;
            Dispatcher.UIThread.Post(() => _ = viewModel.DropFoldersAsync(section: null, folders));
        }
    }
}
