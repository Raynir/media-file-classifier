namespace MediaFileClassifier.Infrastructure.Abstractions.Errors;

/// <summary>
/// Classifies the exceptions of one area, e.g. AI providers or FFmpeg (E05-S04-T01). Each module registers its own
/// as <see cref="IExceptionClassifier"/>, so the classification of a library's exceptions stays next to the code
/// that uses the library.
/// </summary>
public interface IExceptionClassifier
{
    /// <summary>
    /// Classifies an exception this classifier knows.
    /// </summary>
    /// <param name="exception">The exception, never an <see cref="AggregateException"/>.</param>
    /// <returns>The classification, or <see langword="null"/> if the exception isn't one this classifier knows.</returns>
    ClassifiedError? TryClassify(Exception exception);
}
