using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// A line between groups of menu entries.
/// </summary>
public sealed class MenuSeparator : ObservableObject, IMenuEntry
{
    /// <inheritdoc />
    public string Label => "-";

    /// <inheritdoc />
    public string PlainLabel => string.Empty;

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

    /// <inheritdoc />
    IReadOnlyList<IMenuEntry>? IMenuEntry.Items => null;
}
