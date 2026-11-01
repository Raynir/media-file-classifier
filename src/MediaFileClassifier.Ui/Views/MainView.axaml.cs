using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The app shell: menu, navigation, content and details. <see cref="MainWindow"/> hosts it on desktop, and
/// single-view platforms such as Android show it directly (E20).
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        InitializeComponent();
    }
}
