using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// View model of the main window. Navigation is view-model-first: setting
/// <see cref="CurrentPage"/> shows the view that <see cref="ViewLocator"/> maps to it.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Window title.
    /// </summary>
    public string Title => "Media File Classifier";

    /// <summary>
    /// The page shown in the main content area, or <c>null</c> for an empty window.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }
}
