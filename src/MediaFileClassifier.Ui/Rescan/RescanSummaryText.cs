using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;

namespace MediaFileClassifier.Ui.Rescan;

/// <summary>
/// Words the summary of a finished rescan (E02-S04-T06). Full notifications come with E15; this is the simple message
/// the MVP shows.
/// </summary>
public static class RescanSummaryText
{
    /// <summary>
    /// Gets the message for a finished rescan.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="summary">The summary.</param>
    /// <returns>One or two sentences.</returns>
    public static string Describe(Localizer localizer, RescanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.State == ProcessingJobState.Failed)
        {
            return localizer.Get("Rescan_Failed");
        }

        var text = new List<string>();
        if (summary.State == ProcessingJobState.Cancelled)
        {
            text.Add(localizer.Get("Rescan_Cancelled"));
        }

        if (summary.Counts is { } counts)
        {
            if (counts.New + counts.Moved + counts.Missing + summary.Updated == 0)
            {
                text.Add(localizer.Get("Rescan_NothingChanged"));
            }
            else
            {
                text.Add(localizer.Format("Rescan_Done", Number(counts.New), Number(counts.Moved), Number(counts.Missing), Number(summary.Updated)));
            }

            if (counts.Offline > 0)
            {
                text.Add(counts.Offline == 1 ? localizer.Get("Rescan_OfflineNoteOne") : localizer.Format("Rescan_OfflineNote", Number(counts.Offline)));
            }
        }

        return string.Join(' ', text);
    }

    private static string Number(int value) => Format.Number(value);
}
