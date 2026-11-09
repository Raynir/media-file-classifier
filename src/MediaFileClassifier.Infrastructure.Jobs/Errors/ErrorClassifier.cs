using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;

namespace MediaFileClassifier.Infrastructure.Jobs.Errors;

/// <summary>
/// Classifies failures (E05-S04-T01): the registered <see cref="IExceptionClassifier"/>s first, then
/// <see cref="SystemExceptionClassifier"/>, then the inner exception. What nobody recognises is permanent.
/// </summary>
/// <param name="classifiers">Classifiers of the modules, e.g. AI providers and FFmpeg.</param>
public sealed class ErrorClassifier(IEnumerable<IExceptionClassifier> classifiers) : IErrorClassifier
{
    private const int MaxDepth = 8;

    private readonly IReadOnlyList<IExceptionClassifier> classifiers = [.. classifiers, new SystemExceptionClassifier()];

    /// <inheritdoc />
    public ClassifiedError Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The outermost exception that isn't an AggregateException carries the most specific message.
        Exception? outer = null;
        var current = exception;
        for (var depth = 0; depth < MaxDepth && current is not null; depth++)
        {
            if (current is AggregateException aggregate)
            {
                var flattened = aggregate.Flatten();
                current = flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened.InnerException;
                continue;
            }

            outer ??= current;
            foreach (var classifier in classifiers)
            {
                if (classifier.TryClassify(current) is { } classified)
                {
                    // A wrapper's message usually says what was being done; the classifier's says what failed.
                    return ReferenceEquals(current, outer)
                        ? classified with { Exception = exception }
                        : classified with { Exception = exception, Message = outer.Message };
                }
            }

            current = current.InnerException;
        }

        return new ClassifiedError(ErrorCategory.Permanent, (outer ?? exception).Message, exception);
    }
}
