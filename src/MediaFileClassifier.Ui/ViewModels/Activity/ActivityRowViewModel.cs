using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Ui.Activity;
using MediaFileClassifier.UseCases.Activity.GetActivityPage;

namespace MediaFileClassifier.Ui.ViewModels.Activity;

/// <summary>
/// One line of the activity log list: a day header, or an event as two lines (E15-S04-T02).
/// </summary>
public sealed partial class ActivityRowViewModel : ViewModelBase
{
    private ActivityRowViewModel(string key, string? header, ActivityEntry? entry, string title, string detail, string time, string? iconKey, Action<ActivityRowViewModel>? show)
    {
        Key = key;
        Header = header;
        Entry = entry;
        Title = title;
        Detail = detail;
        Time = time;
        if (iconKey is not null
            && Application.Current is { } app
            && app.TryGetResource(iconKey, app.ActualThemeVariant, out var resource)
            && resource is Geometry geometry)
        {
            Icon = geometry;
        }

        ShowItemsCommand = show is null ? null : new RelayCommand(() => show(this));
    }

    /// <summary>
    /// Gets what identifies the row across refreshes: the day, or the event's ID.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the day header's text, or <see langword="null"/> for an event row.
    /// </summary>
    public string? Header { get; }

    /// <summary>
    /// Gets a value indicating whether the row is a day header.
    /// </summary>
    public bool IsHeader => Header is not null;

    /// <summary>
    /// Gets a value indicating whether the row is an event.
    /// </summary>
    public bool IsEvent => Header is null;

    /// <summary>
    /// Gets the event, or <see langword="null"/> for a header.
    /// </summary>
    public ActivityEntry? Entry { get; }

    /// <summary>
    /// Gets the bold first line.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the second line: the subject and the counts.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// Gets the time of day, at the right.
    /// </summary>
    public string Time { get; }

    /// <summary>
    /// Gets the icon of the event's type, when the app's icons are loaded.
    /// </summary>
    public Geometry? Icon { get; }

    /// <summary>
    /// Gets a value indicating whether the event is a warning or an error.
    /// </summary>
    public bool IsProblem => Entry is { Event.Severity: >= ActivitySeverity.Warning };

    /// <summary>
    /// Gets a value indicating whether the row has a "Show items" link.
    /// </summary>
    public bool CanShowItems => ShowItemsCommand is not null && Entry?.ShowFolderId is not null;

    /// <summary>
    /// Gets the "Show items" link's command, or <see langword="null"/> without a navigation.
    /// </summary>
    public IRelayCommand? ShowItemsCommand { get; }

    /// <summary>
    /// Gets the text a screen reader reads for the whole row.
    /// </summary>
    public string AccessibleName => IsHeader ? Header! : $"{Time}, {Title}. {Detail}";

    /// <summary>
    /// Creates a day header.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <param name="text">The header's text.</param>
    /// <returns>The row.</returns>
    public static ActivityRowViewModel ForDay(DateOnly day, string text) =>
        new($"day:{day:yyyyMMdd}", text, null, string.Empty, string.Empty, string.Empty, null, null);

    /// <summary>
    /// Creates an event row.
    /// </summary>
    /// <param name="entry">The event.</param>
    /// <param name="title">The first line.</param>
    /// <param name="detail">The second line.</param>
    /// <param name="time">The time of day.</param>
    /// <param name="show">Runs "Show items".</param>
    /// <returns>The row.</returns>
    public static ActivityRowViewModel ForEvent(ActivityEntry entry, string title, string detail, string time, Action<ActivityRowViewModel>? show)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var iconKey = entry.Event.Severity >= ActivitySeverity.Warning ? "Icon.Status.Error" : ActivityPresentation.IconKey(entry.Event.Type);
        return new($"event:{entry.Event.Id}", null, entry, title, detail, time, iconKey, show);
    }
}
