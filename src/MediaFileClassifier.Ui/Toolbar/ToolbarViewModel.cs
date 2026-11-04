using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Commands;
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
    private readonly ObservableCollection<ToolbarAction> actions = [];
    private readonly ObservableCollection<ObservableObject> items = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolbarViewModel"/> class and collects the contributions.
    /// </summary>
    /// <param name="contributors">The features that fill slots and add actions.</param>
    public ToolbarViewModel(IEnumerable<IToolbarContributor> contributors)
    {
        Actions = new ReadOnlyObservableCollection<ToolbarAction>(actions);
        items.Add(Overflow);
        Items = new ReadOnlyObservableCollection<ObservableObject>(items);
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

    /// <summary>
    /// Gets the action buttons after the slots, left to right.
    /// </summary>
    public ReadOnlyObservableCollection<ToolbarAction> Actions { get; }

    /// <summary>
    /// Gets the overflow menu with the actions that don't fit (E13-S03-T03).
    /// </summary>
    public ToolbarOverflowMenu Overflow { get; } = new();

    /// <summary>
    /// Gets what the action area shows, left to right: the actions, then the overflow menu's More button.
    /// </summary>
    public ReadOnlyObservableCollection<ObservableObject> Items { get; }

    /// <summary>
    /// Adds an action button that runs a command. A command the platform doesn't support
    /// (<see cref="AppCommand.IsVisible"/> is <see langword="false"/>) gets no button.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="order">Position among the actions, left to right in ascending order.</param>
    /// <param name="priority">The lowest priority moves into the overflow menu first.</param>
    /// <exception cref="InvalidOperationException">The command already has a button.</exception>
    public void AddAction(AppCommand command, int order, int priority)
    {
        if (actions.Any(action => action.Command.Id == command.Id))
        {
            throw new InvalidOperationException($"The command '{command.Id}' already has a toolbar button.");
        }

        if (!command.IsVisible)
        {
            return;
        }

        var action = new ToolbarAction(command, order, priority);
        var index = actions.Count(existing => existing.Order <= order);
        actions.Insert(index, action);
        items.Insert(index, action);
        action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolbarAction.IsOverflowed))
            {
                Overflow.Update(actions);
            }
        };
    }
}
