using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Activity;

/// <summary>
/// How the activity log words an event (E15-S04-T02): events store a type and named counts, and the text
/// is made here, so it follows the UI language. Counts the view doesn't know are left out, and missing ones read as zero.
/// </summary>
/// <param name="localizer">UI strings.</param>
public sealed class ActivityPresentation(Localizer localizer)
{
    // The order the counts are listed in; a name here needs a string Activity_Count_<name>.
    private static readonly string[] CountOrder =
    [
        ActivityCounts.Items,
        ActivityCounts.Added,
        ActivityCounts.Modified,
        ActivityCounts.Moved,
        ActivityCounts.Missing,
        ActivityCounts.Folders,
        ActivityCounts.Offline,
        ActivityCounts.Edited,
        ActivityCounts.Restored,
        ActivityCounts.Failed,
    ];

    /// <summary>
    /// Gets the icon resource of an event type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>An icon resource key.</returns>
    public static string IconKey(ActivityEventType type) => type switch
    {
        ActivityEventType.Import => "Icon.FolderAdd",
        ActivityEventType.Rescan => "Icon.Rescan",
        ActivityEventType.Analysis => "Icon.Provenance.Ai",
        ActivityEventType.BulkEdit => "Icon.Provenance.Edited",
        ActivityEventType.FolderAdded => "Icon.FolderAdd",
        ActivityEventType.FolderRemoved => "Icon.Delete",
        ActivityEventType.Export => "Icon.OpenExternal",
        ActivityEventType.LibraryImport => "Icon.Library",
        ActivityEventType.Maintenance => "Icon.Settings",
        ActivityEventType.FolderMonitoring => "Icon.Status.Pending",
        ActivityEventType.FolderRelinked => "Icon.FolderOpen",
        _ => "Icon.Info",
    };

    /// <summary>
    /// Gets the bold first line of an event.
    /// </summary>
    /// <param name="activity">The event.</param>
    /// <returns>The title.</returns>
    public string Title(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Type == ActivityEventType.BulkEdit && activity.Count(ActivityCounts.Restored) > 0 && activity.Count(ActivityCounts.Edited) == 0)
        {
            return localizer.Get("Activity_Title_BulkEditUndone");
        }

        return localizer.Find($"Activity_Title_{activity.Type}") ?? localizer.Get("Activity_Title_Other");
    }

    /// <summary>
    /// Gets the second line of an event: its non-zero counts, then its subject.
    /// </summary>
    /// <param name="activity">The event.</param>
    /// <returns>The detail, or an empty string.</returns>
    public string Detail(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var parts = new List<string>();
        var counts = CountOrder
            .Where(name => activity.Count(name) > 0 && localizer.Find($"Activity_Count_{name}") is not null)
            .Select(name => localizer.Format($"Activity_Count_{name}", activity.Count(name)))
            .ToList();
        if (counts.Count > 0)
        {
            parts.Add(string.Join(localizer.Get("Activity_CountSeparator"), counts));
        }
        else if (activity.Type == ActivityEventType.Rescan && activity.Severity == ActivitySeverity.Info)
        {
            parts.Add(localizer.Get("Activity_NoChanges"));
        }

        // The subject is a long path, so it goes last, where the line trims it.
        if (activity.Subject is { } subject)
        {
            parts.Add(subject);
        }

        return string.Join(localizer.Get("Activity_DetailSeparator"), parts);
    }
}
