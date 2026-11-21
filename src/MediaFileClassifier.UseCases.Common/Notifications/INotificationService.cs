namespace MediaFileClassifier.UseCases.Common.Notifications;

/// <summary>
/// What any feature calls to tell the user something without interrupting them (E15-S01), e.g.
/// <c>notifications.Notify(new Notification("Rescan finished", NotificationSeverity.Success))</c>. It's safe to call
/// from any thread.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a notification, unless it's informational and the user turned those off.
    /// </summary>
    /// <param name="notification">The notification.</param>
    void Notify(Notification notification);
}

/// <summary>
/// Shorthands for <see cref="INotificationService"/>.
/// </summary>
public static class NotificationServiceExtensions
{
    /// <summary>
    /// Shows an informational notification.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="message">The localized text.</param>
    /// <param name="action">The button, or <see langword="null"/>.</param>
    public static void Info(this INotificationService service, string message, NotificationAction? action = null) =>
        Send(service, message, NotificationSeverity.Info, action);

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="message">The localized text.</param>
    /// <param name="action">The button, or <see langword="null"/>.</param>
    public static void Success(this INotificationService service, string message, NotificationAction? action = null) =>
        Send(service, message, NotificationSeverity.Success, action);

    /// <summary>
    /// Shows a warning, which stays until it's dismissed.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="message">The localized text.</param>
    /// <param name="action">The button, or <see langword="null"/>.</param>
    public static void Warning(this INotificationService service, string message, NotificationAction? action = null) =>
        Send(service, message, NotificationSeverity.Warning, action);

    /// <summary>
    /// Shows an error, which stays until it's dismissed.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="message">The localized text.</param>
    /// <param name="action">The button, or <see langword="null"/>.</param>
    public static void Error(this INotificationService service, string message, NotificationAction? action = null) =>
        Send(service, message, NotificationSeverity.Error, action);

    private static void Send(INotificationService service, string message, NotificationSeverity severity, NotificationAction? action)
    {
        ArgumentNullException.ThrowIfNull(service);

        service.Notify(new Notification(message, severity, action));
    }
}
