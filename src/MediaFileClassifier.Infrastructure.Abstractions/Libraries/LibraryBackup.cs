namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// A database backup in a library's <c>backups/</c> folder, such as the one taken before a schema migration
/// (<c>library-v&lt;N&gt;-&lt;yyyyMMdd-HHmmss-fff&gt;.db</c>, E01-S03-T05).
/// </summary>
/// <param name="Path">Full path of the backup.</param>
/// <param name="CreatedAt">When it was taken, from its name.</param>
/// <param name="SchemaVersion">The schema version of the database it holds.</param>
public sealed record LibraryBackup(string Path, DateTimeOffset CreatedAt, int SchemaVersion);
