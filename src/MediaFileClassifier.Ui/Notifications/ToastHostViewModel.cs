using System.Collections.ObjectModel;
using Avalonia.Threading;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Common.Notifications;

namespace MediaFileClassifier.Ui.Notifications;

/// <summary>
/// The toasts in the corner of the main window (E15-S01-T01): at most <see cref="MaxVisible"/> at once, the newest
/// at the bottom. A full stack drops its oldest informational toast to make room; when it holds only warnings and
/// errors, the new notification waits until one is dismissed, so none is lost. Use it on the UI thread.
/// </summary>
public sealed class ToastHostViewModel : ViewModelBase
{
    /// <summary>
    /// The most toasts shown at once.
    /// </summary>
    public const int MaxVisible = 4;

    private readonly ObservableCollection<ToastViewModel> toasts = [];
    private readonly List<Notification> waiting = [];
    private readonly CommandRegistry commands;
    private readonly TimeProvider clock;
    private readonly Action<Action> post;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToastHostViewModel"/> class and starts the job notifications.
    /// </summary>
    /// <param name="service">Raises the notifications.</param>
    /// <param name="commands">Runs the toasts' actions.</param>
    /// <param name="clock">Clock, for the toasts' timers.</param>
    /// <param name="jobs">Raises the notifications for finished jobs; started here, so they exist once the window does.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public ToastHostViewModel(
        NotificationService service,
        CommandRegistry commands,
        TimeProvider clock,
        JobNotifications jobs,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(jobs);

        this.commands = commands;
        this.clock = clock;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        Toasts = new ReadOnlyObservableCollection<ToastViewModel>(toasts);
        service.Raised += (_, notification) => Show(notification);
        jobs.Start();
    }

    /// <summary>
    /// Gets the toasts on screen, oldest first.
    /// </summary>
    public ReadOnlyObservableCollection<ToastViewModel> Toasts { get; }

    /// <summary>
    /// Gets the number of notifications waiting for room.
    /// </summary>
    public int WaitingCount => waiting.Count;

    private void Show(Notification notification)
    {
        if (notification.Key is { } key)
        {
            // The same condition again replaces the earlier toast instead of piling up.
            foreach (var same in toasts.Where(toast => toast.Notification.Key == key).ToList())
            {
                Remove(same);
            }

            waiting.RemoveAll(other => other.Key == key);
        }

        if (toasts.Count >= MaxVisible)
        {
            var oldestInformational = toasts.FirstOrDefault(toast => !toast.IsSticky);
            if (oldestInformational is null)
            {
                waiting.Add(notification);
                OnPropertyChanged(nameof(WaitingCount));
                return;
            }

            Remove(oldestInformational);
        }

        toasts.Add(new ToastViewModel(notification, clock, post, OnDismissed, RunActionAsync));
    }

    private void OnDismissed(ToastViewModel toast)
    {
        Remove(toast);
        if (waiting.Count > 0 && toasts.Count < MaxVisible)
        {
            var next = waiting[0];
            waiting.RemoveAt(0);
            OnPropertyChanged(nameof(WaitingCount));
            Show(next);
        }
    }

    private void Remove(ToastViewModel toast)
    {
        if (toasts.Remove(toast))
        {
            toast.Dispose();
        }
    }

    private async Task RunActionAsync(ToastViewModel toast)
    {
        if (toast.Notification.Action is { } action && commands.TryGet(action.CommandId, out var command))
        {
            await command.ExecuteAsync();
        }
    }
}
