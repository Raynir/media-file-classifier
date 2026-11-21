using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// The window that shows the library information on the desktop (E02-S05-T02).
/// </summary>
public partial class LibraryInfoWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInfoWindow"/> class.
    /// </summary>
    public LibraryInfoWindow()
    {
        InitializeComponent();
        Opened += (_, _) => CloseButton.Focus();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
