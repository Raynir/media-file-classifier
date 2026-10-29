using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// A descriptive value produced by an analysis run. It belongs to the run, not directly to the item,
/// and is deleted with the run when the run is pruned. One per run and field.
/// </summary>
public sealed class AiFieldValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AiFieldValue"/> class.
    /// </summary>
    /// <param name="runId">Run that produced the value.</param>
    /// <param name="itemId">Item the value describes.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="modelId">Model that produced the value.</param>
    /// <param name="confidence">Confidence from 0 to 1, if the model reports one.</param>
    /// <param name="createdAt">When the value was produced.</param>
    public AiFieldValue(
        Id<AnalysisRun> runId,
        Id<MediaItem> itemId,
        DescriptiveField field,
        FieldValue value,
        Id<ModelRegistration> modelId,
        double? confidence,
        DateTimeOffset createdAt)
    {
        DescriptiveFields.EnsureValid(field, value, MetadataSource.AI);
        Confidences.EnsureValid(confidence);

        RunId = runId;
        ItemId = itemId;
        Field = field;
        Value = value;
        ModelId = modelId;
        Confidence = confidence;
        CreatedAt = createdAt;
    }

    public Id<AnalysisRun> RunId { get; }

    public Id<MediaItem> ItemId { get; }

    public DescriptiveField Field { get; }

    public FieldValue Value { get; }

    public Id<ModelRegistration> ModelId { get; }

    public double? Confidence { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Creates a value for a run.
    /// </summary>
    /// <param name="run">Run that produced the value.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="confidence">Confidence from 0 to 1, if any.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The value.</returns>
    public static AiFieldValue For(AnalysisRun run, DescriptiveField field, FieldValue value, double? confidence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new AiFieldValue(run.Id, run.ItemId, field, value, run.ModelId, confidence, now);
    }
}
