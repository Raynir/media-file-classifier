namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// How an analyzer last ended for an item (E06-S01-T03). Stored as a number, so the values never change.
/// </summary>
public enum AnalyzerStatus
{
    /// <summary>
    /// The analyzer ran and its result is stored.
    /// </summary>
    Done = 0,

    /// <summary>
    /// The analyzer failed for good. <see cref="AnalyzerState.IsRetryable"/> says whether a manual retry can help.
    /// </summary>
    Failed = 1,

    /// <summary>
    /// The analyzer didn't run: disabled, no provider, unsupported platform, model missing, or the provider is paused
    /// by its circuit breaker. See <see cref="AnalyzerState.ReasonCode"/>.
    /// </summary>
    Skipped = 2,

    /// <summary>
    /// The analyzer doesn't apply to the item.
    /// </summary>
    NotApplicable = 3,

    /// <summary>
    /// The analyzer failed with a passing problem and the work is retried automatically (E05-S04). It becomes
    /// <see cref="Failed"/> only when the retries give up, so the UI shows "retrying", not an error.
    /// </summary>
    Retrying = 4,
}
