using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using System.ComponentModel;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The app shell: menu, navigation, content and details. <see cref="MainWindow"/> hosts it on desktop, and
/// single-view platforms such as Android show it directly (E20). Files and folders dropped on it are imported (E03-S06).
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        // Handled events too: the search bar takes drops for itself (search by image), and the overlay must hide then.
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);
    }

    // The drawer takes at most the window's width less this, so the scrim stays there to click.
    private const double DrawerGutter = 48;

    // The main areas in F6 order (E16-S03): navigation, toolbar, browser, details.
    private static readonly string[] AreaNames = ["NavigationHost", "ToolbarArea", "BrowserArea", "DetailsHost"];

    private readonly Control?[] lastFocused = new Control?[AreaNames.Length];

    private TopLevel? windowWithHandler;
    private ShellLayout? observedLayout;
    private Control? drawerOpener;

    private DropImportViewModel? Drops => (DataContext as MainWindowViewModel)?.Drops;

    private ShellLayout? Layout => (DataContext as MainWindowViewModel)?.Layout;

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Layout?.UpdateWidth(e.NewSize.Width);
        foreach (var host in new[] { this.FindControl<Panel>("NavigationHost"), this.FindControl<Panel>("DetailsHost") })
        {
            if (host is not null)
            {
                host.MaxWidth = Math.Max(0, e.NewSize.Width - DrawerGutter);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Layout?.UpdateWidth(Bounds.Width);
        if (observedLayout is not null)
        {
            observedLayout.PropertyChanged -= OnLayoutPropertyChanged;
        }

        observedLayout = Layout;
        if (observedLayout is not null)
        {
            observedLayout.PropertyChanged += OnLayoutPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // On the window, so Escape works wherever the focus is, also when it's on the window itself.
        windowWithHandler = TopLevel.GetTopLevel(this);
        windowWithHandler?.AddHandler(KeyDownEvent, OnWindowKeyDown);
        windowWithHandler?.AddHandler(GotFocusEvent, OnWindowGotFocus);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        windowWithHandler?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        windowWithHandler?.RemoveHandler(GotFocusEvent, OnWindowGotFocus);
        windowWithHandler = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // F6 and Shift+F6 move the focus between the main areas (E16-S03).
        if (!e.Handled && e.Key == Key.F6 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift
            && (DataContext as MainWindowViewModel)?.Shortcuts.State.IsOpen != true)
        {
            e.Handled = CycleAreas(e.KeyModifiers == KeyModifiers.Shift);
            return;
        }

        // Escape closes an open drawer (E13-S07), unless something inside, such as a menu or the search box, took it.
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Layout is { IsAnyDrawerOpen: true } layout)
        {
            layout.CloseDrawers();
            e.Handled = true;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        Layout?.CloseDrawers();
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (Drops is not { } drops)
        {
            return;
        }

        var accepts = drops.CanAccept && e.DataTransfer.Contains(DataFormat.File);
        if (e.Handled)
        {
            // Something below, such as the search bar, wants this drop.
            drops.IsDragActive = false;
            return;
        }

        drops.IsDragActive = accepts;
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Drops is { } drops)
        {
            drops.IsDragActive = false;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Drops is not { } drops)
        {
            return;
        }

        drops.IsDragActive = false;
        if (e.Handled || !drops.CanAccept)
        {
            return;
        }

        e.Handled = true;
        List<DroppedPath> paths =
        [
            .. (e.DataTransfer.TryGetFiles() ?? [])
                .Select(item => item.TryGetLocalPath() is { } path ? new DroppedPath(path, item is IStorageFolder) : (DroppedPath?)null)
                .OfType<DroppedPath>(),
        ];

        // After the drop completes: a modal dialog inside the drop callback would block the app the items came from.
        Dispatcher.UIThread.Post(() => _ = drops.DropAsync(paths));
    }

    private static bool CanFocus(InputElement element)
        => element.Focusable && element.IsTabStop && element.IsEffectivelyVisible && element.IsEffectivelyEnabled;

    private static InputElement? FirstFocusable(Control area)
        => area.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(CanFocus);

    private int? AreaOf(Visual? visual)
    {
        for (var node = visual; node is not null; node = node.GetVisualParent())
        {
            var index = Array.FindIndex(AreaNames, name => name == (node as Control)?.Name);
            if (index >= 0)
            {
                return index;
            }
        }

        return null;
    }

    private bool CycleAreas(bool backwards)
    {
        var areas = AreaNames.Select(name => this.FindControl<Control>(name)).ToArray();

        // While a drawer is open the focus stays in it, so only that drawer is in the cycle (E16-S03).
        int? drawer = Layout switch
        {
            { IsAnyDrawerOpen: false } or null => (int?)null,
            { Navigation: { IsDrawer: true, IsDrawerOpen: true } } => 0,
            _ => 3,
        };
        var available = areas
            .Select((area, index) => area is { IsEffectivelyVisible: true } && (drawer is null || drawer == index))
            .ToArray();
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (FocusCycle.Next(available, AreaOf(focused), backwards) is not { } next)
        {
            return false;
        }

        var target = lastFocused[next] is { } last && last.IsAttachedToVisualTree() && CanFocus(last) && AreaOf(last) == next
            ? last
            : FirstFocusable(areas[next]!);
        return target?.Focus(NavigationMethod.Tab) ?? false;
    }

    private void OnWindowGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control control && AreaOf(control) is { } area)
        {
            lastFocused[area] = control;
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellLayout.IsAnyDrawerOpen) || Layout is not { } layout)
        {
            return;
        }

        if (layout.IsAnyDrawerOpen)
        {
            // Opens: remember where the focus was, and move it into the drawer, which then keeps Tab inside.
            drawerOpener ??= TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            var host = this.FindControl<Control>(layout.Navigation is { IsDrawer: true, IsDrawerOpen: true } ? "NavigationHost" : "DetailsHost");
            Dispatcher.UIThread.Post(() => FirstFocusable(host!)?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        }
        else if (drawerOpener is { } opener)
        {
            // Closes: the focus goes back to where it came from, or to the browser when that is gone.
            drawerOpener = null;
            Dispatcher.UIThread.Post(
                () =>
                {
                    var target = opener.IsAttachedToVisualTree() && CanFocus(opener)
                        ? opener
                        : FirstFocusable(this.FindControl<Control>("BrowserArea")!);
                    target?.Focus(NavigationMethod.Tab);
                },
                DispatcherPriority.Loaded);
        }
    }
}
