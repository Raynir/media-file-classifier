using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// A submenu of commands that share an <see cref="AppCommand.Submenu"/>, e.g. View › Navigation panel.
/// </summary>
public sealed class MenuSubmenu : ObservableObject, IMenuEntry
{
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuSubmenu"/> class.
    /// </summary>
    /// <param name="headerKey">Resource key of the header, with an underscore before the access key.</param>
    /// <param name="items">The entries, in order.</param>
    /// <param name="localizer">UI strings.</param>
    public MenuSubmenu(string headerKey, IReadOnlyList<IMenuEntry> items, Localizer localizer)
    {
        this.localizer = localizer;
        HeaderKey = headerKey;
        Items = items;
        localizer.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Gets the resource key of the header.
    /// </summary>
    public string HeaderKey { get; }

    /// <inheritdoc />
    public string Label => localizer.Get(HeaderKey);

    /// <inheritdoc />
    public string PlainLabel => Label.Replace("_", string.Empty, StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<IMenuEntry> Items { get; }

    /// <inheritdoc />
    public bool IsVisible => true;

    /// <inheritdoc />
    ICommand? IMenuEntry.Command => null;

    /// <inheritdoc />
    KeyGesture? IMenuEntry.Gesture => null;

    /// <inheritdoc />
    MenuItemToggleType IMenuEntry.ToggleType => MenuItemToggleType.None;

    /// <inheritdoc />
    bool IMenuEntry.IsChecked => false;

    /// <inheritdoc />
    string? IMenuEntry.DisabledReason => null;

    /// <inheritdoc />
    Geometry? IMenuEntry.Icon => null;
}
