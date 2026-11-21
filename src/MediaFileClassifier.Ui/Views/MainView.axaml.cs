using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The app shell: menu, navigation, content and details. <see cref="MainWindow"/> hosts it on desktop, and
/// single-view platforms such as Android show it directly (E20). Files and folders dropped on it are imported (E03-S06).
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        // Handled events too: the search bar takes drops for itself (search by image), and the overlay must hide then.
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);
    }

    private DropImportViewModel? Drops => (DataContext as MainWindowViewModel)?.Drops;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (Drops is not { } drops)
        {
            return;
        }

        var accepts = drops.CanAccept && e.DataTransfer.Contains(DataFormat.File);
        if (e.Handled)
        {
            // Something below, such as the search bar, wants this drop.
            drops.IsDragActive = false;
            return;
        }

        drops.IsDragActive = accepts;
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Drops is { } drops)
        {
            drops.IsDragActive = false;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Drops is not { } drops)
        {
            return;
        }

        drops.IsDragActive = false;
        if (e.Handled || !drops.CanAccept)
        {
            return;
        }

        e.Handled = true;
        List<DroppedPath> paths =
        [
            .. (e.DataTransfer.TryGetFiles() ?? [])
                .Select(item => item.TryGetLocalPath() is { } path ? new DroppedPath(path, item is IStorageFolder) : (DroppedPath?)null)
                .OfType<DroppedPath>(),
        ];

        // After the drop completes: a modal dialog inside the drop callback would block the app the items came from.
        Dispatcher.UIThread.Post(() => _ = drops.DropAsync(paths));
    }
}
