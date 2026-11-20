using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MediaFileClassifier.Ui.Controls;

/// <summary>
/// The video player's timeline (E14-S06): the track, the played part and a thumb at the position. Clicking or
/// dragging seeks there; the arrow keys step by <see cref="SmallStep"/>, Page Up and Page Down by
/// <see cref="LargeStep"/>, Home and End go to the ends. Seeks go to <see cref="SeekCommand"/> with the
/// <see cref="TimeSpan"/> as parameter; the position comes back from the player.
/// </summary>
public class VideoTimeline : Control
{
    /// <summary>
    /// Defines the <see cref="Duration"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<VideoTimeline, TimeSpan>(nameof(Duration));

    /// <summary>
    /// Defines the <see cref="Position"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<VideoTimeline, TimeSpan>(nameof(Position));

    /// <summary>
    /// Defines the <see cref="SeekCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<VideoTimeline, ICommand?>(nameof(SeekCommand));

    /// <summary>
    /// Defines the <see cref="TrackBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<VideoTimeline, IBrush?>(nameof(TrackBrush), Brushes.Gray);

    /// <summary>
    /// Defines the <see cref="PlayedBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> PlayedBrushProperty =
        AvaloniaProperty.Register<VideoTimeline, IBrush?>(nameof(PlayedBrush), Brushes.White);

    /// <summary>
    /// Defines the <see cref="FocusBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<VideoTimeline, IBrush?>(nameof(FocusBrush), Brushes.DodgerBlue);

    /// <summary>
    /// How far the arrow keys move.
    /// </summary>
    public static readonly TimeSpan SmallStep = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far Page Up and Page Down move.
    /// </summary>
    public static readonly TimeSpan LargeStep = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The track's height.
    /// </summary>
    protected const double TrackHeight = 4;

    /// <summary>
    /// The thumb's radius.
    /// </summary>
    protected const double ThumbRadius = 6;

    private bool dragging;

    static VideoTimeline()
    {
        FocusableProperty.OverrideDefaultValue<VideoTimeline>(true);
        CursorProperty.OverrideDefaultValue<VideoTimeline>(new Cursor(StandardCursorType.Hand));
        AffectsRender<VideoTimeline>(DurationProperty, PositionProperty, TrackBrushProperty, PlayedBrushProperty, FocusBrushProperty);
        AutomationProperties.ControlTypeOverrideProperty.OverrideDefaultValue<VideoTimeline>(Avalonia.Automation.Peers.AutomationControlType.Slider);
    }

    /// <summary>
    /// Gets or sets the length of the video.
    /// </summary>
    public TimeSpan Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the playback position.
    /// </summary>
    public TimeSpan Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the command that seeks; its parameter is the <see cref="TimeSpan"/> to go to.
    /// </summary>
    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the track.
    /// </summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the played part and the thumb.
    /// </summary>
    public IBrush? PlayedBrush
    {
        get => GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the keyboard focus ring around the thumb.
    /// </summary>
    public IBrush? FocusBrush
    {
        get => GetValue(FocusBrushProperty);
        set => SetValue(FocusBrushProperty, value);
    }

    /// <summary>
    /// Gets the horizontal room the track runs in: inset by the thumb, so the thumb stays inside at both ends.
    /// </summary>
    protected (double Left, double Width) Track => (ThumbRadius, Math.Max(0, Bounds.Width - (2 * ThumbRadius)));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var (left, width) = Track;
        var middle = Bounds.Height / 2;
        context.FillRectangle(TrackBrush ?? Brushes.Gray, new Rect(left, middle - (TrackHeight / 2), width, TrackHeight), (float)(TrackHeight / 2));
        if (Duration <= TimeSpan.Zero)
        {
            return;
        }

        var x = XOf(Position);
        var played = PlayedBrush ?? Brushes.White;
        context.FillRectangle(played, new Rect(left, middle - (TrackHeight / 2), x - left, TrackHeight), (float)(TrackHeight / 2));
        RenderOverTrack(context, middle);
        if (IsKeyboardFocusWithin && FocusBrush is { } focus)
        {
            context.DrawEllipse(null, new Pen(focus, 2), new Point(x, middle), ThumbRadius + 3, ThumbRadius + 3);
        }

        context.DrawEllipse(played, null, new Point(x, middle), ThumbRadius, ThumbRadius);
    }

    /// <summary>
    /// Gets the x coordinate of a time on the track.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The x coordinate.</returns>
    protected double XOf(TimeSpan time)
    {
        var (left, width) = Track;
        if (Duration <= TimeSpan.Zero)
        {
            return left;
        }

        return left + (width * Math.Clamp(time / Duration, 0, 1));
    }

    /// <summary>
    /// Gets the time at an x coordinate on the track.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <returns>The time, within the video.</returns>
    protected TimeSpan TimeAt(double x)
    {
        var (left, width) = Track;
        return width <= 0 ? TimeSpan.Zero : Duration * Math.Clamp((x - left) / width, 0, 1);
    }

    /// <summary>
    /// Draws on the track, under the thumb, e.g. markers.
    /// </summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="middle">The track's vertical middle.</param>
    protected virtual void RenderOverTrack(DrawingContext context, double middle)
    {
    }

    /// <summary>
    /// Gets the time a press at a point seeks to; by default the time under it.
    /// </summary>
    /// <param name="point">The point, in the control's coordinates.</param>
    /// <returns>The time.</returns>
    protected virtual TimeSpan PressTarget(Point point) => TimeAt(point.X);

    /// <summary>
    /// Asks <see cref="SeekCommand"/> to seek.
    /// </summary>
    /// <param name="time">The time, clamped to the video.</param>
    protected void RequestSeek(TimeSpan time)
    {
        if (Duration <= TimeSpan.Zero)
        {
            return;
        }

        time = time < TimeSpan.Zero ? TimeSpan.Zero : time > Duration ? Duration : time;
        if (SeekCommand is { } command && command.CanExecute(time))
        {
            command.Execute(time);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Duration <= TimeSpan.Zero)
        {
            return;
        }

        dragging = true;
        e.Pointer.Capture(this);
        Focus(NavigationMethod.Pointer);
        RequestSeek(PressTarget(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging)
        {
            RequestSeek(TimeAt(e.GetPosition(this).X));
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging)
        {
            dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || Duration <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan? target = e.Key switch
        {
            Key.Left or Key.Down => Position - SmallStep,
            Key.Right or Key.Up => Position + SmallStep,
            Key.PageDown => Position - LargeStep,
            Key.PageUp => Position + LargeStep,
            Key.Home => TimeSpan.Zero,
            Key.End => Duration,
            _ => null,
        };
        if (target is { } time)
        {
            RequestSeek(time);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(0, (ThumbRadius + 4) * 2);
}
