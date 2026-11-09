namespace MediaFileClassifier.Infrastructure.Abstractions.Errors;

/// <summary>
/// Classifies any failure (E05-S04-T01). It asks every registered <see cref="IExceptionClassifier"/>, then the built-in
/// rules for .NET exceptions, then the inner exception; a failure nobody recognises is permanent, so a bug isn't
/// retried over and over.
/// </summary>
public interface IErrorClassifier
{
    /// <summary>
    /// Classifies a failure.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>Its category and message.</returns>
    ClassifiedError Classify(Exception exception);
}
