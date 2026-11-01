using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The splitter on the inner edge of a side panel (E13-S01-T01). Dragging it resizes the panel, collapses it
/// when dragged below half its minimum width, and expands it when dragged out again. With focus, the Left and
/// Right keys resize the panel in steps. It never squeezes the media browser below
/// <see cref="ShellLayout.BrowserMinWidth"/>.
/// </summary>
public sealed class PanelSplitter : Border
{
    /// <summary>
    /// How far one key press resizes the panel.
    /// </summary>
    public const double KeyboardStep = 10;

    /// <summary>
    /// The panel the splitter resizes.
    /// </summary>
    public static readonly StyledProperty<SidePanel?> TargetProperty =
        AvaloniaProperty.Register<PanelSplitter, SidePanel?>(nameof(Target));

    /// <summary>
    /// Which side of the window the panel is on: <see cref="Dock.Left"/> or <see cref="Dock.Right"/>.
    /// </summary>
    public static readonly StyledProperty<Dock> PanelSideProperty =
        AvaloniaProperty.Register<PanelSplitter, Dock>(nameof(PanelSide), Dock.Left);

    /// <summary>
    /// The media browser, whose width limits how wide the panel gets.
    /// </summary>
    public static readonly StyledProperty<Control?> BrowserProperty =
        AvaloniaProperty.Register<PanelSplitter, Control?>(nameof(Browser));

    private double? dragStartX;
    private double dragStartWidth;
    private double dragAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelSplitter"/> class.
    /// </summary>
    public PanelSplitter()
    {
        Focusable = true;
    }

    /// <summary>
    /// Gets or sets the panel the splitter resizes.
    /// </summary>
    public SidePanel? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets or sets which side of the window the panel is on.
    /// </summary>
    public Dock PanelSide
    {
        get => GetValue(PanelSideProperty);
        set => SetValue(PanelSideProperty, value);
    }

    /// <summary>
    /// Gets or sets the media browser, whose width limits how wide the panel gets.
    /// </summary>
    public Control? Browser
    {
        get => GetValue(BrowserProperty);
        set => SetValue(BrowserProperty, value);
    }

    // Dragging towards the browser widens a panel on the left, and narrows one on the right.
    private int Direction => PanelSide == Dock.Right ? -1 : 1;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Target is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        dragStartX = PointerX(e);
        dragStartWidth = CurrentWidth();
        dragAvailable = AvailableWidth();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragStartX is { } startX && Target is { } target)
        {
            target.ResizeTo(dragStartWidth + (Direction * (PointerX(e) - startX)), dragAvailable);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragStartX is not null)
        {
            dragStartX = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragStartX = null;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch
        {
            Key.Right => KeyboardStep,
            Key.Left => -KeyboardStep,
            _ => 0,
        };
        if (step != 0 && e.KeyModifiers == KeyModifiers.None && Target is { } target)
        {
            target.ResizeBy(Direction * step, AvailableWidth());
            e.Handled = true;
        }
    }

    // Measured against the window, because the splitter itself moves while the panel resizes.
    private double PointerX(PointerEventArgs e) => e.GetPosition(TopLevel.GetTopLevel(this)).X;

    // The panel host is the splitter's parent; its width is the collapsed width while the panel is collapsed.
    private double CurrentWidth() => (Parent as Control)?.Bounds.Width ?? Target?.Width ?? 0;

    private double AvailableWidth()
        => Browser is null
            ? double.PositiveInfinity
            : CurrentWidth() + Browser.Bounds.Width - ShellLayout.BrowserMinWidth;
}
