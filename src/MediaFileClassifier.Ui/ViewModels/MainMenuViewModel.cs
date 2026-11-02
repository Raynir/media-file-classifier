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
            .Select(group => new MenuGroupViewModel(group, BuildEntries(commands.InGroup(group), localizer), localizer))
            .Where(group => group.Items.Count > 0)];
    }

    /// <summary>
    /// Gets the menu groups, in menu order.
    /// </summary>
    public IReadOnlyList<MenuGroupViewModel> Groups { get; }

    /// <summary>
    /// Lays out a group's commands: commands with a <see cref="AppCommand.Submenu"/> go into that submenu, and
    /// a separator goes between commands whose orders are in different hundreds, e.g. 110 and 200. Inside a
    /// submenu, a separator goes around each run of radio choices.
    /// </summary>
    /// <param name="commands">The visible commands, in order.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The menu entries.</returns>
    public static IReadOnlyList<IMenuEntry> BuildEntries(IReadOnlyList<AppCommand> commands, Localizer localizer)
    {
        var entries = new List<IMenuEntry>();
        int? previousBand = null;
        foreach (var command in commands)
        {
            if (command.Submenu is not null && entries.OfType<MenuSubmenu>().Any(submenu => submenu.HeaderKey == command.Submenu))
            {
                continue;
            }

            var band = command.Order / 100;
            if (previousBand is not null && band != previousBand)
            {
                entries.Add(new MenuSeparator());
            }

            previousBand = band;
            entries.Add(command.Submenu is null
                ? command
                : new MenuSubmenu(command.Submenu, SubmenuEntries(commands.Where(item => item.Submenu == command.Submenu)), localizer));
        }

        return entries;
    }

    private static List<IMenuEntry> SubmenuEntries(IEnumerable<AppCommand> commands)
    {
        var entries = new List<IMenuEntry>();
        bool? previousIsRadio = null;
        foreach (var command in commands)
        {
            var isRadio = command.ToggleType == Avalonia.Controls.MenuItemToggleType.Radio;
            if (previousIsRadio is not null && isRadio != previousIsRadio)
            {
                entries.Add(new MenuSeparator());
            }

            previousIsRadio = isRadio;
            entries.Add(command);
        }

        return entries;
    }
}

/// <summary>
/// One top-level menu and its entries.
/// </summary>
public sealed class MenuGroupViewModel : ViewModelBase
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuGroupViewModel"/> class.
    /// </summary>
    /// <param name="group">One of <see cref="CommandGroups"/>.</param>
    /// <param name="items">The group's entries, in order.</param>
    /// <param name="localizer">UI strings.</param>
    public MenuGroupViewModel(string group, IReadOnlyList<IMenuEntry> items, Localizer localizer)
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
    /// Gets the entries, in order: commands, submenus and separators.
    /// </summary>
    public IReadOnlyList<IMenuEntry> Items { get; }
}
