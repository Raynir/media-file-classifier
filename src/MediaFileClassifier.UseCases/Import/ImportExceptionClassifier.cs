using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;

namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// Classifies <see cref="MediaFileException"/> by its <see cref="MediaFileException.IsPermanent"/> (E05-S04-T01).
/// </summary>
public sealed class ImportExceptionClassifier : IExceptionClassifier
{
    /// <inheritdoc />
    public ClassifiedError? TryClassify(Exception exception) =>
        exception is MediaFileException file
            ? new ClassifiedError(file.IsPermanent ? ErrorCategory.Permanent : ErrorCategory.Transient, file.Message, file)
            : null;
}
