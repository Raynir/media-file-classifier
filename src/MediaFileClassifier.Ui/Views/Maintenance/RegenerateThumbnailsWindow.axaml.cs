using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MediaFileClassifier.Ui.Views.Maintenance;

/// <summary>
/// Asks which thumbnails to regenerate (E02-S06-T02). <c>ShowDialog&lt;bool?&gt;</c> returns <c>true</c> for all
/// thumbnails, <c>false</c> for only the missing or outdated ones, and <c>null</c> when cancelled.
/// </summary>
public partial class RegenerateThumbnailsWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegenerateThumbnailsWindow"/> class.
    /// </summary>
    public RegenerateThumbnailsWindow()
    {
        InitializeComponent();
    }

    private void OnRegenerate(object? sender, RoutedEventArgs e) => Close(AllOption.IsChecked == true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
