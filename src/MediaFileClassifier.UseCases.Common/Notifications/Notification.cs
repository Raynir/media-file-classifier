namespace MediaFileClassifier.UseCases.Common.Notifications;

/// <summary>
/// How important a notification is (E15-S01). Errors and warnings always show and stay until dismissed;
/// informational ones can be turned off in the settings and disappear by themselves (D435).
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Neutral information, e.g. a background task's status.</summary>
    Info,

    /// <summary>Something finished well.</summary>
    Success,

    /// <summary>Something finished, but not everything worked, or needs a look.</summary>
    Warning,

    /// <summary>Something went wrong and needs attention.</summary>
    Error,
}

/// <summary>
/// The button of a notification: it runs an application command, e.g. opens a view.
/// </summary>
/// <param name="Label">The button's text, already localized.</param>
/// <param name="CommandId">The ID of the application command to run.</param>
public sealed record NotificationAction(string Label, string CommandId);

/// <summary>
/// One message for the user, shown as a toast (E15-S01).
/// </summary>
/// <param name="Message">The text, already localized. One summary per job, never one per item.</param>
/// <param name="Severity">How important it is.</param>
/// <param name="Action">What the user can do about it, or <see langword="null"/>.</param>
/// <param name="Key">Identifies what the notification is about, e.g. <c>provider.lm-studio</c>. A new notification
/// replaces an earlier one with the same key, so a repeated condition shows once. <see langword="null"/> for none.</param>
public sealed record Notification(
    string Message,
    NotificationSeverity Severity = NotificationSeverity.Info,
    NotificationAction? Action = null,
    string? Key = null)
{
    /// <summary>
    /// Gets a value indicating whether the notification can be turned off in the settings. Errors and warnings never can.
    /// </summary>
    public bool IsInformational => Severity is NotificationSeverity.Info or NotificationSeverity.Success;
}
