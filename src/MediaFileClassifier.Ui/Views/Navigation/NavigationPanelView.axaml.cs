using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Views.Navigation;

/// <summary>
/// The navigation panel: the sections as a tree when expanded, the icon rail when collapsed. Dropping on the window imports
/// files and folders (E03-S06).
/// </summary>
public partial class NavigationPanelView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationPanelView"/> class.
    /// </summary>
    public NavigationPanelView()
    {
        InitializeComponent();
    }
}
