using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// One entry of a menu built from commands: an <see cref="AppCommand"/>, a <see cref="MenuSubmenu"/> or a
/// <see cref="MenuSeparator"/>. <c>Commands/CommandStyles.axaml</c> binds menu items to these properties.
/// </summary>
public interface IMenuEntry : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the label, with an underscore before the access key, or <c>-</c> for a separator.
    /// </summary>
    string Label { get; }

    /// <summary>
    /// Gets the label without the access-key marker, for accessible names.
    /// </summary>
    string PlainLabel { get; }

    /// <summary>
    /// Gets the command to run, or <c>null</c> for a submenu or separator.
    /// </summary>
    ICommand? Command { get; }

    /// <summary>
    /// Gets the shortcut shown next to the label, or <c>null</c>.
    /// </summary>
    KeyGesture? Gesture { get; }

    /// <summary>
    /// Gets how the entry shows its checked state.
    /// </summary>
    MenuItemToggleType ToggleType { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is checked.
    /// </summary>
    bool IsChecked { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is shown.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets why the entry is disabled, or <c>null</c>.
    /// </summary>
    string? DisabledReason { get; }

    /// <summary>
    /// Gets the icon geometry, or <c>null</c>.
    /// </summary>
    Geometry? Icon { get; }

    /// <summary>
    /// Gets the entries of a submenu, or <c>null</c>.
    /// </summary>
    IReadOnlyList<IMenuEntry>? Items { get; }
}
