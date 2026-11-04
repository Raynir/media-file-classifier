using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// The indexed folders list with the selected folder's rules.
/// </summary>
public partial class IndexedFoldersView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IndexedFoldersView"/> class.
    /// </summary>
    public IndexedFoldersView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AddButton.Focus();
    }
}
