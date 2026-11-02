using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The main window's panel layout (E13-S01), shared by the window and the commands that change it.
/// The sizes follow the Figma screens and are provisional (D095).
/// </summary>
public sealed partial class ShellLayout : ObservableObject
{
    /// <summary>
    /// The narrowest the media browser gets when a side panel is resized.
    /// </summary>
    public const double BrowserMinWidth = 320;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellLayout"/> class with the default layout.
    /// </summary>
    public ShellLayout()
    {
        Navigation.PropertyChanged += OnPanelChanged;
        Details.PropertyChanged += OnPanelChanged;
    }

    /// <summary>
    /// Raised when anything in the layout changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the navigation panel on the left. Collapsed, it's an icon rail.
    /// </summary>
    public SidePanel Navigation { get; } = new(defaultWidth: 240, minWidth: 180, maxWidth: 400);

    /// <summary>
    /// Gets the preview and details panel on the right. Collapsed, it's a strip with its expand button.
    /// </summary>
    public SidePanel Details { get; } = new(defaultWidth: 320, minWidth: 260, maxWidth: 560);

    /// <summary>
    /// Gets or sets a value indicating whether the status bar is shown (E13-S04).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStatusBarVisible { get; set; } = true;

    /// <summary>
    /// Restores the default layout of every panel and the status bar.
    /// </summary>
    public void Reset()
    {
        Navigation.Reset();
        Details.Reset();
        IsStatusBarVisible = true;
    }

    /// <summary>
    /// Gets the layout to save.
    /// </summary>
    /// <returns>The layout.</returns>
    public MainWindowLayout ToSettings() => new(Navigation.ToLayout(), Details.ToLayout(), IsStatusBarVisible);

    /// <summary>
    /// Restores a saved layout.
    /// </summary>
    /// <param name="layout">The saved layout.</param>
    public void Apply(MainWindowLayout layout)
    {
        Navigation.Apply(layout.Navigation);
        Details.Apply(layout.Details);
        IsStatusBarVisible = layout.IsStatusBarVisible;
    }

    partial void OnIsStatusBarVisibleChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
