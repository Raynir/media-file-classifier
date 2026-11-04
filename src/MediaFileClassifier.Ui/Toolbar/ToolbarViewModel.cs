using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// The toolbar above the media browser and details panel (E13-S03, D108). From left to right it has the slots
/// Search, Filter, Sort and View, each filled by the Story that owns the control, and then the action buttons.
/// Set a slot to a view model and the toolbar shows the view that <see cref="ViewLocator"/> maps to it; an empty
/// slot takes no space, except Search, which always fills the free width. Features fill slots through
/// <see cref="IToolbarContributor"/>.
/// </summary>
public sealed partial class ToolbarViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolbarViewModel"/> class and collects the contributions.
    /// </summary>
    /// <param name="contributors">The features that fill slots and add actions.</param>
    public ToolbarViewModel(IEnumerable<IToolbarContributor> contributors)
    {
        foreach (var contributor in contributors)
        {
            contributor.Register(this);
        }
    }

    /// <summary>
    /// Gets or sets the search bar with its similarity and reference image chips (E09-S01, E09-S03, E09-S04).
    /// The slot fills the width the other controls leave.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Search { get; set; }

    /// <summary>
    /// Gets or sets the filter button and the active filter chips (E10-S02).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Filter { get; set; }

    /// <summary>
    /// Gets or sets the sort control (E10-S04).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Sort { get; set; }

    /// <summary>
    /// Gets or sets the view controls: the thumbnail size slider (E14-S01-T05), and later the grid/list toggle
    /// (E14-S02).
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? View { get; set; }
}
