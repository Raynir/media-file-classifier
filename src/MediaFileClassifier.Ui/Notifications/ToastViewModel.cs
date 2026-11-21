using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.UseCases.Common.Notifications;

namespace MediaFileClassifier.Ui.Notifications;

/// <summary>
/// One toast on screen (E15-S01-T01). Informational toasts dismiss themselves after <see cref="AutoDismissAfter"/>,
/// pausing while the pointer or the keyboard focus is on them. Warnings and errors stay until they're dismissed.
/// </summary>
public sealed partial class ToastViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long an informational toast stays.
    /// </summary>
    public static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(6);

    private readonly Action<ToastViewModel> dismiss;
    private readonly Func<ToastViewModel, Task> runAction;
    private readonly ITimer? timer;
    private bool pointerOver;
    private bool focused;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToastViewModel"/> class and starts its timer.
    /// </summary>
    /// <param name="notification">What to show.</param>
    /// <param name="clock">Clock, for the timer.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="dismiss">Removes the toast.</param>
    /// <param name="runAction">Runs the toast's action.</param>
    public ToastViewModel(
        Notification notification,
        TimeProvider clock,
        Action<Action> post,
        Action<ToastViewModel> dismiss,
        Func<ToastViewModel, Task> runAction)
    {
        Notification = notification;
        this.dismiss = dismiss;
        this.runAction = runAction;
        if (!IsSticky)
        {
            timer = clock.CreateTimer(_ => post(Dismiss), null, AutoDismissAfter, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Gets the notification.
    /// </summary>
    public Notification Notification { get; }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Message => Notification.Message;

    /// <summary>
    /// Gets the severity.
    /// </summary>
    public NotificationSeverity Severity => Notification.Severity;

    /// <summary>
    /// Gets the icon geometry from <c>Theming/Icons.axaml</c>, or <see langword="null"/> where there's no application.
    /// </summary>
    public Geometry? Icon =>
        Application.Current?.TryGetResource(IsSticky ? "Icon.Status.Error" : "Icon.Info", null, out var resource) == true
            ? resource as Geometry
            : null;

    /// <summary>
    /// Gets the action button's text, or <see langword="null"/> when there's no action.
    /// </summary>
    public string? ActionLabel => Notification.Action?.Label;

    /// <summary>
    /// Gets a value indicating whether the toast has an action.
    /// </summary>
    public bool HasAction => Notification.Action is not null;

    /// <summary>
    /// Gets a value indicating whether the toast stays until it's dismissed.
    /// </summary>
    public bool IsSticky => !Notification.IsInformational;

    /// <summary>
    /// Gets a value indicating whether the toast is a warning.
    /// </summary>
    public bool IsWarning => Severity == NotificationSeverity.Warning;

    /// <summary>
    /// Gets a value indicating whether the toast is an error.
    /// </summary>
    public bool IsError => Severity == NotificationSeverity.Error;

    /// <summary>
    /// Tells the toast whether the pointer is on it. The timer stops while the pointer or the focus is on the toast.
    /// </summary>
    /// <param name="isOver"><see langword="true"/> while the pointer is over the toast.</param>
    public void SetPointerOver(bool isOver)
    {
        pointerOver = isOver;
        UpdateTimer();
    }

    /// <summary>
    /// Tells the toast whether the focus is inside it.
    /// </summary>
    /// <param name="hasFocus"><see langword="true"/> while the focus is inside the toast.</param>
    public void SetFocusWithin(bool hasFocus)
    {
        focused = hasFocus;
        UpdateTimer();
    }

    /// <summary>
    /// Removes the toast.
    /// </summary>
    [RelayCommand]
    public void Dismiss() => dismiss(this);

    /// <summary>
    /// Runs the action, then removes the toast.
    /// </summary>
    /// <returns>A task that completes when the action has run.</returns>
    [RelayCommand]
    public async Task RunActionAsync()
    {
        try
        {
            await runAction(this);
        }
        finally
        {
            Dismiss();
        }
    }

    /// <inheritdoc />
    public void Dispose() => timer?.Dispose();

    // Paused while either holds it; the full time starts again once both let go.
    private void UpdateTimer() => timer?.Change(pointerOver || focused ? Timeout.InfiniteTimeSpan : AutoDismissAfter, Timeout.InfiniteTimeSpan);
}
