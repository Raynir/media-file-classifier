using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MediaFileClassifier.Ui.Search;

namespace MediaFileClassifier.Ui.Views.Search;

/// <summary>
/// The search box in the toolbar (E09-S01-T05). A picture dropped on it or pasted into it starts an image-to-image
/// search (E09-S04-T01). The drop is handled here, so it never reaches a drop target behind the toolbar, such as the
/// library import.
/// </summary>
public partial class SearchBarView : UserControl
{
    /// <summary>
    /// The name a pasted bitmap shows on the chip.
    /// </summary>
    internal const string PastedImageName = "Pasted image.png";

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchBarView"/> class.
    /// </summary>
    public SearchBarView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SearchBox.PastingFromClipboard += OnPasting;
    }

    private static bool HasPicture(IDataTransfer data) =>
        data.Contains(DataFormat.File) || data.Contains(DataFormat.Bitmap);

    private static async Task UseFileAsync(SearchBarViewModel viewModel, IStorageFile file)
    {
        PickedImage picture;
        try
        {
            picture = await ReferenceImageFiles.ReadAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.ShowUnreadableImage();
            return;
        }

        await viewModel.UseImageAsync(picture);
    }

    private static void UseBitmap(SearchBarViewModel viewModel, Bitmap bitmap, string name)
    {
        _ = viewModel.UseImageAsync(ReferenceImageFiles.FromBitmap(bitmap, name));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasPicture(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // Whatever was dropped on the search bar is a search, never an import.
        e.Handled = true;
        if (DataContext is not SearchBarViewModel viewModel)
        {
            return;
        }

        if (e.DataTransfer.TryGetFile() is IStorageFile file)
        {
            _ = UseFileAsync(viewModel, file);
        }
        else if (e.DataTransfer.TryGetBitmap() is { } bitmap)
        {
            UseBitmap(viewModel, bitmap, PastedImageName);
        }
    }

    // A picture on the clipboard replaces the text paste; anything else pastes as text, as usual.
    private void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SearchBarViewModel viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        e.Handled = true;
        _ = PasteAsync(viewModel, clipboard);
    }

    private async Task PasteAsync(SearchBarViewModel viewModel, IClipboard clipboard)
    {
        if (await clipboard.TryGetFileAsync() is IStorageFile file)
        {
            await UseFileAsync(viewModel, file);
            return;
        }

        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            UseBitmap(viewModel, bitmap, PastedImageName);
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            SearchBox.SelectedText = text;
        }
    }
}
