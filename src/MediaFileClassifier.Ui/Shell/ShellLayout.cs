using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Which main window panels are shown. Shared by the window and the commands that toggle the panels.
/// Saving the layout across restarts comes with E13-S01.
/// </summary>
public sealed partial class ShellLayout : ObservableObject
{
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
}
