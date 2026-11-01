using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// One execution of one analyzer on one item (D020). AI field values and AI tag assignments belong to a run,
/// not directly to the item.
/// </summary>
/// <remarks>
/// <para>
/// Each item has at most one <see cref="IsActive">active</see> run per analyzer, which provides the current AI layer.
/// Re-analysis creates a new run that becomes active, and the previous run is kept so the results can be compared.
/// The item's metadata aggregate decides which run is active and which runs are pruned.
/// </para>
/// <para>
/// Invariants: the analyzer and provider are not blank, only a completed run can be active or rejected,
/// and a finished run has a completion date.
/// </para>
/// </remarks>
public sealed class AnalysisRun
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisRun"/> class from stored values.
    /// </summary>
    /// <param name="id">Run ID.</param>
    /// <param name="itemId">Analyzed item.</param>
    /// <param name="analyzer">Analyzer key, e.g. <c>caption</c>, <c>ocr</c> or <c>transcript</c>.</param>
    /// <param name="providerId">Provider that ran the model.</param>
    /// <param name="modelId">Model used.</param>
    /// <param name="modelVersion">Model version reported at run time, if any.</param>
    /// <param name="promptVersion">Version of the prompt or analyzer settings, if any.</param>
    /// <param name="startedAt">When the run started.</param>
    /// <param name="completedAt">When the run finished, if it did.</param>
    /// <param name="status">Execution status.</param>
    /// <param name="error">Failure message, if the run failed.</param>
    /// <param name="isActive">Whether the run provides the item's current AI layer for its analyzer.</param>
    /// <param name="rejectedAt">When the user rejected the run's results in review (D021), if they did.</param>
    public AnalysisRun(
        Id<AnalysisRun> id,
        Id<MediaItem> itemId,
        string analyzer,
        string providerId,
        Id<ModelRegistration> modelId,
        string? modelVersion,
        string? promptVersion,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        AnalysisRunStatus status,
        string? error,
        bool isActive,
        DateTimeOffset? rejectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (status != AnalysisRunStatus.Running && completedAt is null)
        {
            throw new ArgumentException("A finished run needs a completion date.", nameof(completedAt));
        }

        if ((isActive || rejectedAt is not null) && status != AnalysisRunStatus.Completed)
        {
            throw new ArgumentException("Only a completed run can be active or rejected.", nameof(status));
        }

        Id = id;
        ItemId = itemId;
        Analyzer = analyzer;
        ProviderId = providerId;
        ModelId = modelId;
        ModelVersion = modelVersion;
        PromptVersion = promptVersion;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Status = status;
        Error = error;
        IsActive = isActive;
        RejectedAt = rejectedAt;
    }

    public Id<AnalysisRun> Id { get; }

    public Id<MediaItem> ItemId { get; }

    public string Analyzer { get; }

    public string ProviderId { get; }

    public Id<ModelRegistration> ModelId { get; }

    public string? ModelVersion { get; }

    public string? PromptVersion { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public AnalysisRunStatus Status { get; private set; }

    public string? Error { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    public bool IsRejected => RejectedAt is not null;

    /// <summary>
    /// Gets a value indicating whether the run's values are part of the item's effective AI layer:
    /// it's active, completed and not rejected.
    /// </summary>
    public bool ProvidesAiLayer => IsActive && Status == AnalysisRunStatus.Completed && !IsRejected;

    /// <summary>
    /// Starts a new run.
    /// </summary>
    /// <param name="itemId">Item to analyze.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="model">Model the analyzer uses.</param>
    /// <param name="promptVersion">Version of the prompt or analyzer settings, if any.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The running run.</returns>
    public static AnalysisRun Start(
        Id<MediaItem> itemId,
        string analyzer,
        ModelRegistration model,
        string? promptVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new AnalysisRun(
            Id<AnalysisRun>.New(),
            itemId,
            analyzer,
            model.ProviderId,
            model.Id,
            model.Version,
            promptVersion,
            now,
            null,
            AnalysisRunStatus.Running,
            null,
            false,
            null);
    }

    public void Complete(DateTimeOffset now) => Finish(AnalysisRunStatus.Completed, null, now);

    public void Fail(string error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Finish(AnalysisRunStatus.Failed, error, now);
    }

    public RunReference ToReference() => new(Id, Analyzer, ModelId, PromptVersion, StartedAt);

    /// <summary>
    /// Marks the run as rejected in review (D021): its AI text and tags stop being effective.
    /// </summary>
    /// <param name="now">Current time.</param>
    internal void Reject(DateTimeOffset now)
    {
        EnsureCompleted();
        RejectedAt ??= now;
    }

    internal void Activate()
    {
        EnsureCompleted();
        IsActive = true;
    }

    internal void Deactivate() => IsActive = false;

    private void Finish(AnalysisRunStatus status, string? error, DateTimeOffset now)
    {
        if (Status != AnalysisRunStatus.Running)
        {
            throw new InvalidOperationException($"Run {Id} has already finished.");
        }

        Status = status;
        Error = error;
        CompletedAt = now;
    }

    private void EnsureCompleted()
    {
        if (Status != AnalysisRunStatus.Completed)
        {
            throw new InvalidOperationException($"Run {Id} isn't completed.");
        }
    }
}
