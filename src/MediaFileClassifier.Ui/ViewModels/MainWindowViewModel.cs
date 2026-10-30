using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// View model of the main window. Navigation is view-model-first: setting
/// <see cref="CurrentPage"/> shows the view that <see cref="ViewLocator"/> maps to it.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// The page shown in the main content area, or <c>null</c> for an empty window.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    /// <summary>
    /// Whether the navigation panel on the left is expanded. Collapsed, it shows only its icons.
    /// </summary>
    [ObservableProperty]
    public partial bool IsNavigationExpanded { get; set; } = true;

    /// <summary>
    /// Whether the details panel on the right is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailsPanelOpen { get; set; } = true;

    /// <summary>
    /// Expands or collapses the navigation panel.
    /// </summary>
    [RelayCommand]
    private void ToggleNavigation() => IsNavigationExpanded = !IsNavigationExpanded;

    /// <summary>
    /// Shows or hides the details panel.
    /// </summary>
    [RelayCommand]
    private void ToggleDetailsPanel() => IsDetailsPanelOpen = !IsDetailsPanelOpen;
}
