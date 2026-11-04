using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.StatusBar;

/// <summary>
/// One segment of the status bar (E13-S04-T01), such as the result count or the processing indicator. The
/// status bar shows its icon and text, and runs <see cref="Command"/> when it's clicked. A segment that needs
/// its own visuals sets <see cref="Content"/> to a view model that <see cref="ViewLocator"/> maps to a view.
/// Add segments through an <see cref="IStatusBarContributor"/>.
/// </summary>
public sealed partial class StatusBarSegment : ObservableObject
{
    private Geometry? icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarSegment"/> class.
    /// </summary>
    /// <param name="id">Unique ID, e.g. <c>browser.counts</c>.</param>
    /// <param name="side">Which end of the status bar the segment sits at.</param>
    /// <param name="order">Position from that end, in ascending order: from the left edge on the left side,
    /// and from the right edge on the right side.</param>
    public StatusBarSegment(string id, StatusBarSide side, int order)
    {
        Id = id;
        Side = side;
        Order = order;
    }

    /// <summary>
    /// Gets the unique ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets which end of the status bar the segment sits at.
    /// </summary>
    public StatusBarSide Side { get; }

    /// <summary>
    /// Gets the position from that end.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets or sets the text, already localized.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string? Text { get; set; }

    /// <summary>
    /// Gets or sets the resource key of the icon in <c>Theming/Icons.axaml</c>, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    public partial string? IconKey { get; set; }

    /// <summary>
    /// Gets or sets the tooltip, already localized, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string? ToolTip { get; set; }

    /// <summary>
    /// Gets or sets what clicking the segment runs, e.g. an <see cref="Commands.AppCommand"/>, or <c>null</c>
    /// for a segment that only shows information.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    public partial ICommand? Command { get; set; }

    /// <summary>
    /// Gets or sets custom content shown instead of the icon and text, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? Content { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the segment is shown. Hide a segment while it has nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets the icon geometry, or <c>null</c>.
    /// </summary>
    public Geometry? Icon => icon ??= IconKey is not null
        && Application.Current?.TryGetResource(IconKey, null, out var resource) == true
            ? resource as Geometry
            : null;

    /// <summary>
    /// Gets a value indicating whether clicking the segment does something.
    /// </summary>
    public bool IsClickable => Command is not null;

    /// <summary>
    /// Gets the name read by screen readers: the tooltip, or else the text.
    /// </summary>
    public string? AccessibleName => ToolTip ?? Text;

    partial void OnIconKeyChanged(string? value) => icon = null;
}
