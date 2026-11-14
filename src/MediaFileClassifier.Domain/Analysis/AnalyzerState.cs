using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// How one analyzer last ended for one item (E06-S01-T03): one row per item and analyzer, replaced on every run. The
/// pipeline uses it to skip work that's already done on a retry, and the UI (E06-S07) and manual retry (E05-S04) use
/// it to find failed analyzers.
/// </summary>
/// <remarks>Invariants: a failure, a skip and a retry have a reason; only <see cref="AnalyzerStatus.Done"/> has a run.</remarks>
public sealed record AnalyzerState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzerState"/> class.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key, see <see cref="AnalyzerKeys"/>.</param>
    /// <param name="status">How the analyzer ended.</param>
    /// <param name="reasonCode">Why it was skipped or failed, see <see cref="AnalyzerReasons"/>.</param>
    /// <param name="detail">More about the failure, e.g. the provider's message.</param>
    /// <param name="failureKind">The AI failure kind (<c>AiFailureKind</c> name), when a provider failed.</param>
    /// <param name="isRetryable">Whether running the analyzer again can help.</param>
    /// <param name="runId">The run that stored the result, for an analyzer that stores one. Not a foreign key, since
    /// runs are pruned.</param>
    /// <param name="updatedAt">When the state was written.</param>
    public AnalyzerState(
        Id<MediaItem> itemId,
        string analyzer,
        AnalyzerStatus status,
        string? reasonCode,
        string? detail,
        string? failureKind,
        bool isRetryable,
        Id<AnalysisRun>? runId,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);
        if (status != AnalyzerStatus.Done && string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException($"A {status} analyzer needs a reason.", nameof(reasonCode));
        }

        if (status != AnalyzerStatus.Done && runId is not null)
        {
            throw new ArgumentException("Only a finished analyzer has a run.", nameof(runId));
        }

        ItemId = itemId;
        Analyzer = analyzer;
        Status = status;
        ReasonCode = status == AnalyzerStatus.Done ? null : reasonCode;
        Detail = detail;
        FailureKind = failureKind;
        IsRetryable = isRetryable;
        RunId = runId;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Gets the item.
    /// </summary>
    public Id<MediaItem> ItemId { get; }

    /// <summary>
    /// Gets the analyzer key.
    /// </summary>
    public string Analyzer { get; }

    /// <summary>
    /// Gets how the analyzer ended.
    /// </summary>
    public AnalyzerStatus Status { get; }

    /// <summary>
    /// Gets why it was skipped or failed, see <see cref="AnalyzerReasons"/>.
    /// </summary>
    public string? ReasonCode { get; }

    /// <summary>
    /// Gets more about the failure, e.g. the provider's message.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets the AI failure kind, when a provider failed.
    /// </summary>
    public string? FailureKind { get; }

    /// <summary>
    /// Gets a value indicating whether running the analyzer again can help.
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>
    /// Gets the run that stored the result, if the analyzer stores one.
    /// </summary>
    public Id<AnalysisRun>? RunId { get; }

    /// <summary>
    /// Gets when the state was written.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Creates the state of an analyzer that ran and stored its result.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="runId">The run, if the analyzer stores one.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The state.</returns>
    public static AnalyzerState Done(Id<MediaItem> itemId, string analyzer, Id<AnalysisRun>? runId, DateTimeOffset now) =>
        new(itemId, analyzer, AnalyzerStatus.Done, null, null, null, false, runId, now);

    /// <summary>
    /// Creates the state of an analyzer that didn't run.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reasonCode">Why, see <see cref="AnalyzerReasons"/>.</param>
    /// <param name="isRetryable">Whether running it again can help, e.g. once a model is installed.</param>
    /// <param name="now">Current time.</param>
    /// <param name="detail">More about it.</param>
    /// <returns>The state.</returns>
    public static AnalyzerState Skipped(Id<MediaItem> itemId, string analyzer, string reasonCode, bool isRetryable, DateTimeOffset now, string? detail = null) =>
        new(itemId, analyzer, AnalyzerStatus.Skipped, reasonCode, detail, null, isRetryable, null, now);

    /// <summary>
    /// Creates the state of an analyzer that doesn't apply to the item.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reasonCode">Why.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The state.</returns>
    public static AnalyzerState NotApplicable(Id<MediaItem> itemId, string analyzer, string reasonCode, DateTimeOffset now) =>
        new(itemId, analyzer, AnalyzerStatus.NotApplicable, reasonCode, null, null, false, null, now);

    /// <summary>
    /// Creates the state of an analyzer that failed.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reasonCode">Why, see <see cref="AnalyzerReasons"/>.</param>
    /// <param name="detail">The failure's message.</param>
    /// <param name="failureKind">The AI failure kind, when a provider failed.</param>
    /// <param name="isRetryable">Whether a manual retry can help.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The state.</returns>
    public static AnalyzerState Failed(
        Id<MediaItem> itemId,
        string analyzer,
        string reasonCode,
        string? detail,
        string? failureKind,
        bool isRetryable,
        DateTimeOffset now) =>
        new(itemId, analyzer, AnalyzerStatus.Failed, reasonCode, detail, failureKind, isRetryable, null, now);

    /// <summary>
    /// Creates the state of an analyzer whose passing failure is retried automatically.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reasonCode">Why, see <see cref="AnalyzerReasons"/>.</param>
    /// <param name="detail">The failure's message.</param>
    /// <param name="failureKind">The AI failure kind, when a provider failed.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The state.</returns>
    public static AnalyzerState Retrying(Id<MediaItem> itemId, string analyzer, string reasonCode, string? detail, string? failureKind, DateTimeOffset now) =>
        new(itemId, analyzer, AnalyzerStatus.Retrying, reasonCode, detail, failureKind, true, null, now);

    /// <summary>
    /// Turns a retrying state into a final failure, once the automatic retries give up.
    /// </summary>
    /// <param name="isRetryable">Whether a manual retry can help.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The failed state.</returns>
    public AnalyzerState ToFailed(bool isRetryable, DateTimeOffset now) =>
        Failed(ItemId, Analyzer, ReasonCode ?? AnalyzerReasons.ProviderFailed, Detail, FailureKind, isRetryable, now);
}
