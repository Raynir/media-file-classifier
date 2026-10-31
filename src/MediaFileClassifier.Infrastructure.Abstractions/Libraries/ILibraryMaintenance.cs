namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Integrity check, online backup and optimization of an open library's database (E01-S03-T07).
/// </summary>
public interface ILibraryMaintenance
{
    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c>. Reads and writes can continue meanwhile.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IntegrityCheckResult> CheckIntegrityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the database to a new file with <c>VACUUM INTO</c> (D073). Reads and writes can continue meanwhile;
    /// the copy holds the committed state of the moment the backup started. It's compacted and self-contained.
    /// </summary>
    /// <param name="destinationPath">Path of the backup file, which must not exist yet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the backup is written.</returns>
    Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the query planner statistics, and optionally rebuilds the database to reclaim free space. It's queued
    /// like any other write.
    /// </summary>
    /// <param name="vacuum">Whether to run <c>VACUUM</c> too, which rewrites the whole file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the optimization is done.</returns>
    Task OptimizeAsync(bool vacuum = false, CancellationToken cancellationToken = default);
}
