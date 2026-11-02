using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// A modal question with two answers. <c>ShowDialog&lt;bool&gt;</c> returns <c>true</c> for the primary answer.
/// </summary>
public partial class LibraryMessageWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMessageWindow"/> class. Used by the XAML designer only.
    /// </summary>
    public LibraryMessageWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMessageWindow"/> class.
    /// </summary>
    /// <param name="message">The question.</param>
    /// <param name="primary">The primary answer, e.g. "Restore".</param>
    /// <param name="secondary">The other answer, e.g. "Keep as is".</param>
    public LibraryMessageWindow(string message, string primary, string secondary)
        : this()
    {
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
    }

    private void OnPrimary(object? sender, RoutedEventArgs e) => Close(true);

    private void OnSecondary(object? sender, RoutedEventArgs e) => Close(false);
}
