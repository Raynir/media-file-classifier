using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// The create-library steps: name and location, then folders.
/// </summary>
public partial class CreateLibraryView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateLibraryView"/> class.
    /// </summary>
    public CreateLibraryView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameBox.Focus();
    }
}
