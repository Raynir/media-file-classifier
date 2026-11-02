using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The main window areas that other Epics fill with their views (E13-S01-T01). Set a slot to a view model,
/// and the shell shows the view that <see cref="ViewLocator"/> maps to it. An empty slot shows the area's
/// empty state.
/// </summary>
public sealed partial class ShellRegions : ObservableObject
{
    /// <summary>
    /// Gets or sets the navigation panel's content: libraries, collections and saved searches (E13-S02).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Navigation { get; set; }

    /// <summary>
    /// Gets or sets the search and toolbar area above the media browser (E13-S03). While it's empty, the area
    /// takes no space.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Toolbar { get; set; }

    /// <summary>
    /// Gets or sets the media browser in the centre (E14).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Browser { get; set; }

    /// <summary>
    /// Gets or sets the preview and details panel's content (E14).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Details { get; set; }
}
