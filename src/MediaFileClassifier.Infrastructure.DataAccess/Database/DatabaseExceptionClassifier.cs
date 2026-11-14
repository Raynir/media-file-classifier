using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// Classifies SQLite failures (E05-S04-T01): a busy or locked database passes; other database errors aren't
/// classified here and fall through to the wrapped exception or the default.
/// </summary>
public sealed class DatabaseExceptionClassifier : IExceptionClassifier
{
    private const int Busy = 5;
    private const int Locked = 6;

    /// <inheritdoc />
    public ClassifiedError? TryClassify(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: Busy or Locked }
            ? new ClassifiedError(ErrorCategory.Transient, exception.Message, exception)
            : null;
}
