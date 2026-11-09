using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// The "LM Studio unavailable" warning at the right end of the status bar (E05-S04-T05), shown while an AI provider's
/// circuit is open (E05-S04-T03), so the user knows why analysis waits. Its tooltip says what's wrong; clicking it
/// checks the providers again at once instead of waiting for the next periodic check. Provisional, like the rest of
/// the status bar (D095).
/// </summary>
public sealed class ProviderStatusSegment : IStatusBarContributor
{
    /// <summary>
    /// The segment ID.
    /// </summary>
    public const string SegmentId = "processing.providers";

    private readonly IProviderCircuitBreaker? breaker;
    private readonly Localizer localizer;
    private readonly Action<Action> post;
    private StatusBarSegment? segment;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderStatusSegment"/> class.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="breaker">Knows which providers are unavailable, or <see langword="null"/> where there's no job system.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public ProviderStatusSegment(Localizer localizer, IProviderCircuitBreaker? breaker = null, Action<Action>? post = null)
    {
        this.localizer = localizer;
        this.breaker = breaker;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
    }

    /// <inheritdoc />
    public void Register(StatusBarViewModel statusBar)
    {
        ArgumentNullException.ThrowIfNull(statusBar);

        if (breaker is null)
        {
            return;
        }

        segment = statusBar.Add(new StatusBarSegment(SegmentId, StatusBarSide.Right, 2)
        {
            IconKey = "Icon.Status.Error",
            IsVisible = false,
            Command = new AsyncRelayCommand(() => breaker.CheckNowAsync()),
        });
        breaker.Changed += (_, _) => post(Show);
        localizer.Changed += (_, _) => Show();
        Show();
    }

    private void Show()
    {
        if (segment is null || breaker is null)
        {
            return;
        }

        var open = breaker.OpenCircuits;
        segment.IsVisible = open.Count > 0;
        if (open.Count == 0)
        {
            return;
        }

        segment.Text = open.Count == 1
            ? localizer.Format("StatusBar_ProviderUnavailable_One", open[0].DisplayName)
            : localizer.Format("StatusBar_ProviderUnavailable_Other", Format.Number(open.Count));
        var lines = open.Select(status => open.Count == 1 ? status.Message : $"{status.DisplayName}: {status.Message}");
        var hint = open.All(status => status.IsConfiguration)
            ? localizer.Get("StatusBar_ProviderUnavailable_FixSettings")
            : localizer.Get("StatusBar_ProviderUnavailable_ToolTip");
        segment.ToolTip = string.Join(Environment.NewLine, lines.Append(hint));
    }
}
