using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// Puts an integrity check result in plain language (E02-S06-T03). SQLite's own messages go to the log, not to the user.
/// </summary>
public static class IntegrityReportText
{
    /// <summary>
    /// Describes the result.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="report">The result.</param>
    /// <returns>The report.</returns>
    public static string Describe(Localizer localizer, IntegrityReport report)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(report);

        return report.IsOk
            ? localizer.Get("Integrity_Healthy")
            : localizer.Get("Integrity_Damaged");
    }
}
