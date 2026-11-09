namespace MediaFileClassifier.Infrastructure.Jobs.Errors;

/// <summary>
/// When transient failures are retried (E05-S04-T02): exponential backoff with full jitter, up to a number of attempts.
/// </summary>
/// <param name="MaxAttempts">The most times a work item runs, the first run included.</param>
/// <param name="BaseDelay">The longest wait before the first retry; it doubles with each retry.</param>
/// <param name="MaxDelay">The longest wait before any retry.</param>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)
{
    /// <summary>
    /// Gets the default policy: 5 attempts, waiting up to about 2, 4, 8 and 16 seconds, at most 1 minute.
    /// </summary>
    public static RetryPolicy Default { get; } = new(5, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

    /// <summary>
    /// Gets the random number source for the jitter; replace it in tests.
    /// </summary>
    public Func<double> NextRandom { get; init; } = Random.Shared.NextDouble;

    /// <summary>
    /// Gets whether a work item that used some retries may retry once more.
    /// </summary>
    /// <param name="retryCount">The retries used so far.</param>
    /// <returns><see langword="true"/> if it may.</returns>
    public bool CanRetry(int retryCount) => retryCount + 1 < MaxAttempts;

    /// <summary>
    /// Gets how long to wait before a retry. Full jitter (a random wait up to the backoff) keeps many items that failed
    /// together from retrying together.
    /// </summary>
    /// <param name="retryCount">The retries used before this one.</param>
    /// <returns>The wait.</returns>
    public TimeSpan GetDelay(int retryCount)
    {
        var backoff = BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(retryCount, 0, 30));
        var cap = Math.Min(backoff, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cap * Math.Clamp(NextRandom(), 0, 1));
    }
}
