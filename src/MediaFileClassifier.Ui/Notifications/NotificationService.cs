using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Common.Notifications;

namespace MediaFileClassifier.Ui.Notifications;

/// <summary>
/// Takes notifications from any thread (E15-S01-T01), drops informational ones while the user turned them off (D435:
/// errors and warnings always show), and hands the rest to the toast host on the UI thread.
/// </summary>
/// <param name="settings">Holds the <see cref="KnownSettings.InformationalNotifications"/> setting.</param>
/// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
public sealed class NotificationService(ISettingsStore settings, Action<Action>? post = null) : INotificationService
{
    private readonly Action<Action> post = post ?? (action =>
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    });

    /// <summary>
    /// Raised on the UI thread for every notification that is to be shown.
    /// </summary>
    public event EventHandler<Notification>? Raised;

    /// <inheritdoc />
    public void Notify(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.IsInformational && !settings.Get(KnownSettings.InformationalNotifications))
        {
            return;
        }

        post(() => Raised?.Invoke(this, notification));
    }
}
