using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// Modal progress while a library's database is backed up and migrated (E02-S02-T05). The app closes it.
/// </summary>
public partial class LibraryProgressWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryProgressWindow"/> class.
    /// </summary>
    public LibraryProgressWindow()
    {
        InitializeComponent();
    }
}
