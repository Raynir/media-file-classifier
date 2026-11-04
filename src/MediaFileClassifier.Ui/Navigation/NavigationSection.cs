using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// A section of the navigation panel (E13-S02-T01), such as Library or Folders, with its entries. A section can have
/// an action in its header and accept dropped folders. Collapsing it is remembered with the layout.
/// </summary>
public sealed class NavigationSection : NavigationNode
{
    private Geometry? actionIcon;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationSection"/> class, expanded.
    /// </summary>
    /// <param name="key">Identifies the section, e.g. for its saved collapsed state.</param>
    /// <param name="title">The title shown.</param>
    /// <param name="order">Where it goes: lower first.</param>
    public NavigationSection(string key, string title, int order)
    {
        Key = key;
        Title = title;
        Order = order;
        IsExpanded = true;
    }

    /// <summary>
    /// Gets the key that identifies the section.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the title shown.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets where the section goes: lower first.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets the entries.
    /// </summary>
    public ObservableCollection<NavigationEntry> Entries { get; } = [];

    /// <summary>
    /// Gets the action in the header, e.g. Manage folders, or <c>null</c>.
    /// </summary>
    public ICommand? Action { get; init; }

    /// <summary>
    /// Gets the label of <see cref="Action"/>, for its tooltip and accessible name.
    /// </summary>
    public string? ActionLabel { get; init; }

    /// <summary>
    /// Gets the resource key of the icon of <see cref="Action"/>.
    /// </summary>
    public string? ActionIconKey { get; init; }

    /// <summary>
    /// Gets the icon of <see cref="Action"/>, or <c>null</c>.
    /// </summary>
    public Geometry? ActionIcon => actionIcon ??= ActionIconKey is not null && Application.Current?.TryGetResource(ActionIconKey, null, out var resource) == true
        ? resource as Geometry
        : null;

    /// <summary>
    /// Gets a value indicating whether the section has an action.
    /// </summary>
    public bool HasAction => Action is not null;

    /// <summary>
    /// Gets what to do with folders dropped on the section, or <c>null</c> if it doesn't take them.
    /// </summary>
    public Func<IReadOnlyList<FolderHandle>, Task>? DropFolders { get; init; }
}
