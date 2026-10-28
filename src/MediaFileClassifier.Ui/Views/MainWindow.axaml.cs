namespace MediaFileClassifier.Ui.Views;

using Avalonia;
using Avalonia.Controls;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The main application window. Restores and saves its placement through <see cref="ISettingsService"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService? settingsService;
    private PixelPoint normalPosition;
    private Size normalSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class. Used by the XAML designer only.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="settingsService">App settings.</param>
    public MainWindow(ISettingsService settingsService)
        : this()
    {
        this.settingsService = settingsService;
        RestorePlacement(settingsService.Current.MainWindow);
        PositionChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();
        Opened += (_, _) => KeepOnScreen();
        Closing += (_, _) => SavePlacement();
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

    // A saved position can be off-screen after a monitor was disconnected.
    private void KeepOnScreen()
    {
        if (Screens.ScreenFromPoint(Position) is null && Screens.Primary is { } primary)
        {
            Position = primary.WorkingArea.TopLeft;
        }
    }

    private void RememberNormalBounds()
    {
        if (WindowState == WindowState.Normal)
        {
            normalPosition = Position;
            normalSize = new Size(Width, Height);
        }
    }

    private void SavePlacement()
    {
        if (settingsService is null)
        {
            return;
        }

        RememberNormalBounds();
        var placement = new WindowPlacement(
            normalPosition.X,
            normalPosition.Y,
            normalSize.Width,
            normalSize.Height,
            WindowState == WindowState.Maximized);
        settingsService.Save(settingsService.Current with { MainWindow = placement });
    }
}
