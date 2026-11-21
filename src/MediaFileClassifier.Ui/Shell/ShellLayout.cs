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

    /// <summary>The details panel preview's default height, as in Figma (E14-S05).</summary>
    public const double DefaultPreviewHeight = 200;

    /// <summary>The details panel preview's smallest height.</summary>
    public const double MinPreviewHeight = 120;

    /// <summary>The details panel preview's largest height.</summary>
    public const double MaxPreviewHeight = 1200;

    private readonly HashSet<string> collapsedSections = new(StringComparer.Ordinal);

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
    /// Gets the layout for the window's width (E13-S07). It isn't saved: the saved panel layout is the wide one.
    /// </summary>
    public LayoutBreakpoint Breakpoint { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a side panel is open as a drawer over the browser.
    /// </summary>
    public bool IsAnyDrawerOpen => (Navigation.IsDrawer && Navigation.IsDrawerOpen) || (Details.IsDrawer && Details.IsDrawerOpen);

    /// <summary>
    /// Switches the panels to the layout for a window width (E13-S07): wide docks both, medium makes the navigation
    /// an icon rail and the details a drawer, and narrow makes both drawers. Drawers close when the layout changes.
    /// </summary>
    /// <param name="width">The window's width.</param>
    public void UpdateWidth(double width)
    {
        var breakpoint = LayoutBreakpoints.For(width);
        if (breakpoint == Breakpoint)
        {
            return;
        }

        Breakpoint = breakpoint;
        CloseDrawers();
        Navigation.Mode = breakpoint switch
        {
            LayoutBreakpoint.Wide => PanelMode.Docked,
            LayoutBreakpoint.Medium => PanelMode.Rail,
            _ => PanelMode.Drawer,
        };
        Details.Mode = breakpoint == LayoutBreakpoint.Wide ? PanelMode.Docked : PanelMode.Drawer;
        OnPropertyChanged(nameof(Breakpoint));
        OnPropertyChanged(nameof(IsAnyDrawerOpen));
    }

    /// <summary>
    /// Closes the open drawer, if any.
    /// </summary>
    public void CloseDrawers()
    {
        Navigation.IsDrawerOpen = false;
        Details.IsDrawerOpen = false;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the status bar is shown (E13-S04).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStatusBarVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets the details panel preview's height (E14-S05).
    /// </summary>
    [ObservableProperty]
    public partial double DetailsPreviewHeight { get; set; } = DefaultPreviewHeight;

    /// <summary>
    /// Gets or sets a value indicating whether the details panel's preview is collapsed (E14-S05).
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailsPreviewCollapsed { get; set; }

    /// <summary>
    /// Gets or sets the details panel's open tab (E14-S05).
    /// </summary>
    [ObservableProperty]
    public partial DetailsTab DetailsTab { get; set; }

    /// <summary>
    /// Gets the keys of the navigation panel's collapsed sections (E13-S02).
    /// </summary>
    public IReadOnlySet<string> CollapsedNavigationSections => collapsedSections;

    /// <summary>
    /// Collapses or expands a navigation section, remembered with the layout.
    /// </summary>
    /// <param name="key">The section key.</param>
    /// <param name="collapsed">Whether it's collapsed.</param>
    public void SetNavigationSectionCollapsed(string key, bool collapsed)
    {
        if (collapsed ? collapsedSections.Add(key) : collapsedSections.Remove(key))
        {
            OnPropertyChanged(nameof(CollapsedNavigationSections));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Restores the default layout of every panel and the status bar.
    /// </summary>
    public void Reset()
    {
        Navigation.Reset();
        Details.Reset();
        IsStatusBarVisible = true;
        DetailsPreviewHeight = DefaultPreviewHeight;
        IsDetailsPreviewCollapsed = false;
        DetailsTab = DetailsTab.Overview;
        if (collapsedSections.Count > 0)
        {
            collapsedSections.Clear();
            OnPropertyChanged(nameof(CollapsedNavigationSections));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the layout to save.
    /// </summary>
    /// <returns>The layout.</returns>
    public MainWindowLayout ToSettings() =>
        new(
            Navigation.ToLayout(),
            Details.ToLayout(),
            IsStatusBarVisible,
            collapsedSections.Count == 0 ? null : [.. collapsedSections.Order(StringComparer.Ordinal)],
            new DetailsPanelSections(DetailsPreviewHeight, IsDetailsPreviewCollapsed, DetailsTab == DetailsTab.Technical ? "technical" : "overview"));

    /// <summary>
    /// Restores a saved layout.
    /// </summary>
    /// <param name="layout">The saved layout.</param>
    public void Apply(MainWindowLayout layout)
    {
        Navigation.Apply(layout.Navigation);
        Details.Apply(layout.Details);
        IsStatusBarVisible = layout.IsStatusBarVisible;
        DetailsPreviewHeight = Math.Clamp(layout.DetailsSections?.PreviewHeight ?? DefaultPreviewHeight, MinPreviewHeight, MaxPreviewHeight);
        IsDetailsPreviewCollapsed = layout.DetailsSections?.IsPreviewCollapsed ?? false;
        DetailsTab = layout.DetailsSections?.Tab == "technical" ? DetailsTab.Technical : DetailsTab.Overview;
        collapsedSections.Clear();
        collapsedSections.UnionWith(layout.CollapsedNavigationSections ?? []);
        OnPropertyChanged(nameof(CollapsedNavigationSections));
    }

    partial void OnIsStatusBarVisibleChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnDetailsPreviewHeightChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnIsDetailsPreviewCollapsedChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnDetailsTabChanged(DetailsTab value) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidePanel.IsDrawerOpen) && sender is SidePanel { IsDrawerOpen: true } opened)
        {
            // One drawer at a time.
            var other = ReferenceEquals(opened, Navigation) ? Details : Navigation;
            other.IsDrawerOpen = false;
        }

        if (e.PropertyName is nameof(SidePanel.IsDrawerOpen) or nameof(SidePanel.IsDrawer) or nameof(SidePanel.Mode))
        {
            OnPropertyChanged(nameof(IsAnyDrawerOpen));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The details panel's tabs (E14-S05).
/// </summary>
public enum DetailsTab
{
    /// <summary>The item's descriptive metadata.</summary>
    Overview,

    /// <summary>The file and its technical metadata.</summary>
    Technical,
}
