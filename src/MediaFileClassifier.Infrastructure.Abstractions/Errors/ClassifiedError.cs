using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Abstractions.Errors;

/// <summary>
/// A failure with its category (E05-S04-T01), as returned by <see cref="IErrorClassifier"/>.
/// </summary>
/// <param name="Category">Whether the failure is transient, permanent or a configuration problem.</param>
/// <param name="Message">What went wrong, for the user.</param>
/// <param name="Exception">The exception that was classified.</param>
public sealed record ClassifiedError(ErrorCategory Category, string Message, Exception Exception)
{
    /// <summary>
    /// Gets the AI provider that failed, if the failure came from one.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets whether the failure says the provider itself is in trouble (unreachable, timing out, server errors, not
    /// configured), so it counts for the provider's circuit breaker. A provider rejecting one input doesn't.
    /// </summary>
    public bool IsProviderFailure { get; init; }

    /// <summary>
    /// Gets whether the failure is a model that isn't installed. Such work waits for the model (D037) instead of
    /// failing, and never counts for the circuit breaker.
    /// </summary>
    public bool IsModelMissing { get; init; }

    /// <summary>
    /// Gets whether the item's folder is Offline (D188): the work waits until the folder is Online
    /// again, and the item keeps its status (D025).
    /// </summary>
    public bool IsFolderOffline { get; init; }
}
