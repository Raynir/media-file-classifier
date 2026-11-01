using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// The main menu (E13-S05-T02): the groups Library, Edit, View, Item, Tools and Help, filled from the
/// command registry. A group without commands is left out until its features register some.
/// </summary>
public sealed class MainMenuViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainMenuViewModel"/> class.
    /// </summary>
    /// <param name="commands">The command registry.</param>
    /// <param name="localizer">UI strings.</param>
    public MainMenuViewModel(CommandRegistry commands, Localizer localizer)
    {
        Groups = [.. CommandGroups.All
            .Select(group => new MenuGroupViewModel(group, commands.InGroup(group), localizer))
            .Where(group => group.Items.Count > 0)];
    }

    /// <summary>
    /// Gets the menu groups, in menu order.
    /// </summary>
    public IReadOnlyList<MenuGroupViewModel> Groups { get; }
}

/// <summary>
/// One top-level menu and its commands.
/// </summary>
public sealed class MenuGroupViewModel : ViewModelBase
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuGroupViewModel"/> class.
    /// </summary>
    /// <param name="group">One of <see cref="CommandGroups"/>.</param>
    /// <param name="items">The group's commands, in order.</param>
    /// <param name="localizer">UI strings.</param>
    public MenuGroupViewModel(string group, IReadOnlyList<AppCommand> items, Localizer localizer)
    {
        this.localizer = localizer;
        Group = group;
        Items = items;
        localizer.Changed += (_, _) => OnPropertyChanged(nameof(Header));
    }

    /// <summary>
    /// Gets the group, one of <see cref="CommandGroups"/>.
    /// </summary>
    public string Group { get; }

    /// <summary>
    /// Gets the menu header, with an underscore before the access key.
    /// </summary>
    public string Header => localizer.Get(CommandGroups.HeaderKey(Group));

    /// <summary>
    /// Gets the commands, in order.
    /// </summary>
    public IReadOnlyList<AppCommand> Items { get; }
}
