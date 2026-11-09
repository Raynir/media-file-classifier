using MediaFileClassifier.Infrastructure.Abstractions.Errors;

namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Stops sending work to an AI provider that keeps failing (E05-S04-T03). After a few consecutive provider failures
/// (one for a configuration problem) its circuit opens: work that needs it is parked instead of failing, and its
/// health is checked periodically. When it's healthy again the circuit closes and the parked work runs.
/// </summary>
/// <remarks>
/// A stage that uses several providers checks <see cref="IsOpen"/> before calling one, so the others keep working,
/// and throws an <see cref="AiProviderException"/> for the skipped one so its work item is parked. A missing model
/// never opens a circuit (D037).
/// </remarks>
public interface IProviderCircuitBreaker
{
    /// <summary>
    /// Raised, on any thread, when a circuit opens or closes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets the providers whose circuit is open, oldest first.
    /// </summary>
    IReadOnlyList<ProviderCircuitStatus> OpenCircuits { get; }

    /// <summary>
    /// Gets whether a provider's circuit is open, so it shouldn't be called.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    /// <returns><see langword="true"/> while the provider is unavailable.</returns>
    bool IsOpen(string providerId);

    /// <summary>
    /// Records a successful call, which resets the provider's count of consecutive failures.
    /// </summary>
    /// <param name="providerId">Provider instance ID.</param>
    void RecordSuccess(string providerId);

    /// <summary>
    /// Records a failure. Only provider failures (<see cref="ClassifiedError.IsProviderFailure"/>) count.
    /// </summary>
    /// <param name="error">The classified failure.</param>
    /// <returns>Whether the failing provider's circuit is open now, so the work should be parked.</returns>
    bool RecordFailure(ClassifiedError error);

    /// <summary>
    /// Checks the health of every provider whose circuit is open now, instead of waiting for the next periodic check.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the checks are done.</returns>
    Task CheckNowAsync(CancellationToken cancellationToken = default);
}
