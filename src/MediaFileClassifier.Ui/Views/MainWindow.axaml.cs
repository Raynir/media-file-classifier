using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.Views;

/// <summary>
/// The main application window. Restores and saves its placement for the current monitor setup through
/// <see cref="LayoutStore"/> (E13-S01-T03).
/// </summary>
public partial class MainWindow : Window
{
    private readonly LayoutStore? layoutStore;
    private readonly Size defaultSize;
    private PixelPoint normalPosition;
    private Size normalSize;
    private bool isPlacementRestored;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class. Used by the XAML designer only.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        defaultSize = new Size(Width, Height);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="layoutStore">Saves and restores the layout.</param>
    /// <param name="commands">The commands, whose shortcuts the window binds.</param>
    public MainWindow(LayoutStore layoutStore, CommandRegistry commands)
        : this()
    {
        this.layoutStore = layoutStore;
        foreach (var command in commands.All.Where(command => command.Gesture is not null))
        {
            KeyBindings.Add(new KeyBinding { Gesture = command.Gesture!, Command = command });
        }

        RestorePlacement(layoutStore.PlacementFor(CurrentMonitorSetup()));
        PositionChanged += (_, _) => RememberPlacement();
        SizeChanged += (_, _) => RememberPlacement();
        Opened += (_, _) => OnOpened();
        Closing += (_, _) =>
        {
            RememberPlacement();
            layoutStore.Flush();
        };
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            RememberPlacement();
        }
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        normalPosition = Position;
        normalSize = new Size(Width, Height);
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnOpened()
    {
        if (WindowState == WindowState.Normal)
        {
            normalPosition = Position;
            normalSize = new Size(Width, Height);
        }

        KeepOnScreen();
        isPlacementRestored = true;
        RememberPlacement();
    }

    // A saved position can be off-screen after a monitor was disconnected: fall back to the default placement.
    private void KeepOnScreen()
    {
        var placement = CurrentPlacement();
        if (Screens.Primary is not { } primary || MonitorSetup.IsReachable(placement, primary.Scaling, Screens.All.Select(screen => screen.WorkingArea)))
        {
            return;
        }

        var size = new Size(
            Math.Min(defaultSize.Width, primary.WorkingArea.Width / primary.Scaling),
            Math.Min(defaultSize.Height, primary.WorkingArea.Height / primary.Scaling));
        Width = size.Width;
        Height = size.Height;
        Position = primary.WorkingArea.CenterRect(PixelRect.FromRect(new Rect(size), primary.Scaling)).Position;
        normalPosition = Position;
        normalSize = size;
    }

    private void RememberPlacement()
    {
        if (layoutStore is null || !isPlacementRestored)
        {
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            normalPosition = Position;
            normalSize = new Size(Width, Height);
        }

        layoutStore.RememberPlacement(CurrentMonitorSetup(), CurrentPlacement());
    }

    private WindowPlacement CurrentPlacement() => new(
        normalPosition.X,
        normalPosition.Y,
        normalSize.Width,
        normalSize.Height,
        WindowState == WindowState.Maximized);

    private string CurrentMonitorSetup() => MonitorSetup.Fingerprint(Screens.All.Select(screen => (screen.Bounds, screen.Scaling)));
}
