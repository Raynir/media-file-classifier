namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Result of a database integrity check.
/// </summary>
/// <param name="Problems">Problems SQLite reported; empty if the database is intact.</param>
public sealed record IntegrityCheckResult(IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Gets a value indicating whether the database is intact.
    /// </summary>
    public bool IsOk => Problems.Count == 0;
}
