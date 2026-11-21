using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;
using MediaFileClassifier.UseCases.Maintenance.RestoreBackup;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// Puts an integrity check result and a restore in plain language (E02-S06-T03). SQLite's own messages go to the log,
/// not to the user.
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

        if (report.IsOk)
        {
            return localizer.Get("Integrity_Healthy");
        }

        var next = report.Backup is { } backup
            ? localizer.Format("Integrity_BackupAvailable", When(backup))
            : localizer.Get("Integrity_NoBackup");
        return localizer.Get("Integrity_Damaged") + "\n\n" + next;
    }

    /// <summary>
    /// Describes how restoring a backup ended.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="backup">The backup.</param>
    /// <param name="result">How it ended.</param>
    /// <returns>The message.</returns>
    public static string DescribeRestore(Localizer localizer, LibraryBackup backup, RestoreIntegrityBackupResult result)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(result);

        return result.Failure switch
        {
            null when result.Report is { IsOk: false } => localizer.Format("Integrity_RestoredDamaged", When(backup)),
            null => localizer.Format("Integrity_Restored", When(backup)),
            RestoreBackupFailure.NotPutBack => localizer.Format("Integrity_NotPutBack", result.DamagedPath),
            { } failure => localizer.Format("Integrity_RestoreFailed", localizer.Get("Integrity_Reason_" + failure)),
        };
    }

    private static DateTime When(LibraryBackup backup) => backup.CreatedAt.LocalDateTime;
}
