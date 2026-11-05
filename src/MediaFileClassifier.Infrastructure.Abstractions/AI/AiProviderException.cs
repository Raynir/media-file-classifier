namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// A provider call failed. Capability methods throw only this (or <see cref="OperationCanceledException"/> when the
/// caller cancels), so the pipeline records a failure instead of crashing.
/// </summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException()
    {
    }

    public AiProviderException(string message)
        : base(message)
    {
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiProviderException"/> class.
    /// </summary>
    /// <param name="kind">Why the call failed.</param>
    /// <param name="providerId">Provider instance that failed.</param>
    /// <param name="message">What happened, for the user and the log.</param>
    /// <param name="rawOutput">The model's raw answer, for <see cref="AiFailureKind.InvalidOutput"/>.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AiProviderException(AiFailureKind kind, string providerId, string message, string? rawOutput = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderId = providerId;
        RawOutput = rawOutput;
    }

    public AiFailureKind Kind { get; } = AiFailureKind.ProviderError;

    public string? ProviderId { get; }

    public string? RawOutput { get; }
}
