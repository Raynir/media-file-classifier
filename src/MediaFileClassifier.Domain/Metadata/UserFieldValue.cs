using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// A descriptive value owned by the user (D020). It always wins over AI and technical values,
/// and re-analysis never replaces it. One per item and field.
/// </summary>
/// <remarks>
/// Invariants: an <see cref="UserValueOrigin.Accepted"/> value references the run it was accepted from,
/// and an <see cref="UserValueOrigin.Edited"/> value doesn't.
/// </remarks>
public sealed class UserFieldValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserFieldValue"/> class.
    /// </summary>
    /// <param name="itemId">Item the value describes.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="origin">How the user created the value.</param>
    /// <param name="acceptedFrom">Run an accepted value came from.</param>
    /// <param name="updatedAt">When the user last changed the value.</param>
    /// <param name="suggestionRunId">Newer run with a different AI value for the field, if there is one.</param>
    public UserFieldValue(
        Id<MediaItem> itemId,
        DescriptiveField field,
        FieldValue value,
        UserValueOrigin origin,
        RunReference? acceptedFrom,
        DateTimeOffset updatedAt,
        Id<AnalysisRun>? suggestionRunId)
    {
        DescriptiveFields.EnsureValid(field, value, MetadataSource.User);
        if ((origin == UserValueOrigin.Accepted) != (acceptedFrom is not null))
        {
            throw new ArgumentException("Only an accepted value references the run it came from.", nameof(acceptedFrom));
        }

        ItemId = itemId;
        Field = field;
        Value = value;
        Origin = origin;
        AcceptedFrom = acceptedFrom;
        UpdatedAt = updatedAt;
        SuggestionRunId = suggestionRunId;
    }

    public Id<MediaItem> ItemId { get; }

    public DescriptiveField Field { get; }

    public FieldValue Value { get; }

    public UserValueOrigin Origin { get; }

    /// <summary>
    /// Gets the run an accepted value came from: model, prompt version and date, for comparing with newer AI results.
    /// </summary>
    public RunReference? AcceptedFrom { get; }

    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Gets the newer run whose AI value differs from this value: a new AI suggestion the user can accept or dismiss (D020).
    /// </summary>
    public Id<AnalysisRun>? SuggestionRunId { get; }

    public bool HasNewAiSuggestion => SuggestionRunId is not null;

    public static UserFieldValue Edited(Id<MediaItem> itemId, DescriptiveField field, FieldValue value, DateTimeOffset now) =>
        new(itemId, field, value, UserValueOrigin.Edited, null, now, null);

    public static UserFieldValue Accepted(Id<MediaItem> itemId, DescriptiveField field, FieldValue value, RunReference run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new(itemId, field, value, UserValueOrigin.Accepted, run, now, null);
    }

    internal UserFieldValue WithSuggestion(Id<AnalysisRun>? runId) =>
        new(ItemId, Field, Value, Origin, AcceptedFrom, UpdatedAt, runId);
}
