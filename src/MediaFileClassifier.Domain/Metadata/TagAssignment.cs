using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Text;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// One tag on one item, with its source. Tags are stored as individual assignments, so each keeps its own provenance.
/// </summary>
/// <remarks>
/// Invariants: an AI assignment belongs to a run and records its model; a user assignment records its origin,
/// and an accepted one references the run it was accepted from; a technical assignment has neither. A tag can carry
/// its form in the other language (D035, D047): written by the model, or derived from the tag dictionary (D348) and
/// then marked as derived.
/// </remarks>
public sealed class TagAssignment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TagAssignment"/> class.
    /// </summary>
    /// <param name="itemId">Tagged item.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="source">Layer the assignment comes from.</param>
    /// <param name="runId">Run that produced an AI assignment.</param>
    /// <param name="modelId">Model that produced an AI assignment.</param>
    /// <param name="confidence">AI confidence from 0 to 1, if any.</param>
    /// <param name="origin">How the user created a user assignment.</param>
    /// <param name="acceptedFrom">Run an accepted user assignment came from.</param>
    /// <param name="createdAt">When the assignment was made.</param>
    /// <param name="otherForm">The tag in the other language: Russian for an English tag, English for a Russian one.</param>
    /// <param name="isOtherFormDerived">Whether the other form comes from the tag dictionary or a translation.</param>
    public TagAssignment(
        Id<MediaItem> itemId,
        TagName tag,
        MetadataSource source,
        Id<AnalysisRun>? runId,
        Id<ModelRegistration>? modelId,
        double? confidence,
        UserValueOrigin? origin,
        RunReference? acceptedFrom,
        DateTimeOffset createdAt,
        string? otherForm = null,
        bool isOtherFormDerived = false)
    {
        ArgumentNullException.ThrowIfNull(tag);
        // Lenient, so a stray stored value can't stop an item loading: a blank form is no form, and only a form is derived.
        otherForm = string.IsNullOrWhiteSpace(otherForm) ? null : otherForm;
        isOtherFormDerived &= otherForm is not null;
        Confidences.EnsureValid(confidence);

        var valid = source switch
        {
            MetadataSource.AI => runId is not null && modelId is not null && origin is null && acceptedFrom is null,
            MetadataSource.User => runId is null && modelId is null && origin is not null
                && (origin == UserValueOrigin.Accepted) == (acceptedFrom is not null),
            _ => runId is null && modelId is null && origin is null && acceptedFrom is null,
        };
        if (!valid)
        {
            throw new ArgumentException($"The provenance doesn't fit a {source} tag assignment.", nameof(source));
        }

        ItemId = itemId;
        Tag = tag;
        Source = source;
        RunId = runId;
        ModelId = modelId;
        Confidence = confidence;
        Origin = origin;
        AcceptedFrom = acceptedFrom;
        CreatedAt = createdAt;
        OtherForm = otherForm?.Trim();
        IsOtherFormDerived = isOtherFormDerived;
    }

    /// <summary>
    /// Creates an empty instance for EF Core to fill when it loads the assignment (D071). EF Core can't pass the
    /// complex-typed values to the public constructor.
    /// </summary>
    private TagAssignment() => Tag = null!;

    public Id<MediaItem> ItemId { get; }

    public TagName Tag { get; }

    public MetadataSource Source { get; }

    public Id<AnalysisRun>? RunId { get; }

    public Id<ModelRegistration>? ModelId { get; }

    public double? Confidence { get; }

    public UserValueOrigin? Origin { get; }

    public RunReference? AcceptedFrom { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the tag in the other language, if known.
    /// </summary>
    public string? OtherForm { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="OtherForm"/> was derived from the tag dictionary or a translation,
    /// rather than written with the tag.
    /// </summary>
    public bool IsOtherFormDerived { get; }

    /// <summary>
    /// Makes a tag read from the file. Its other form is derived from the tag dictionary.
    /// </summary>
    /// <param name="itemId">Tagged item.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The assignment.</returns>
    public static TagAssignment Technical(Id<MediaItem> itemId, TagName tag, DateTimeOffset now)
    {
        var otherForm = Derive(tag);
        return new(itemId, tag, MetadataSource.Technical, null, null, null, null, null, now, otherForm, otherForm is not null);
    }

    /// <summary>
    /// Makes a tag of an AI run. Without a form the model wrote, its other form is derived from the tag dictionary. A
    /// written form equal to the tag (ignoring case) isn't a form, so the dictionary's is used.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="confidence">AI confidence from 0 to 1, if any.</param>
    /// <param name="now">Current time.</param>
    /// <param name="otherForm">The other-language form the model wrote, if any.</param>
    /// <returns>The assignment.</returns>
    public static TagAssignment Ai(AnalysisRun run, TagName tag, double? confidence, DateTimeOffset now, string? otherForm = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        var written = string.IsNullOrWhiteSpace(otherForm) || string.Equals(otherForm.Trim(), tag.Value, StringComparison.OrdinalIgnoreCase)
            ? null
            : otherForm.Trim();
        var form = written ?? Derive(tag);
        return new(run.ItemId, tag, MetadataSource.AI, run.Id, run.ModelId, confidence, null, null, now, form, written is null && form is not null);
    }

    /// <summary>
    /// Makes a tag the user wrote. The tag is stored as typed, and its other form is derived from the tag dictionary
    /// (D047).
    /// </summary>
    /// <param name="itemId">Tagged item.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The assignment.</returns>
    public static TagAssignment UserEdited(Id<MediaItem> itemId, TagName tag, DateTimeOffset now)
    {
        var otherForm = Derive(tag);
        return new(itemId, tag, MetadataSource.User, null, null, null, UserValueOrigin.Edited, null, now, otherForm, otherForm is not null);
    }

    /// <summary>
    /// Makes a tag the user accepted from an AI run. It copies the AI tag's other form (D047); if that has none, the form
    /// is derived from the tag dictionary.
    /// </summary>
    /// <param name="itemId">Tagged item.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="run">The run it was accepted from.</param>
    /// <param name="now">Current time.</param>
    /// <param name="accepted">The AI assignment accepted, if known.</param>
    /// <returns>The assignment.</returns>
    public static TagAssignment UserAccepted(Id<MediaItem> itemId, TagName tag, RunReference run, DateTimeOffset now, TagAssignment? accepted = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        var (form, derived) = accepted?.OtherForm is { } copied ? (copied, accepted.IsOtherFormDerived) : (Derive(tag), true);
        return new(itemId, tag, MetadataSource.User, null, null, null, UserValueOrigin.Accepted, run, now, form, derived && form is not null);
    }

    private static string? Derive(TagName tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return TagDictionary.Default.Translate(tag.Value);
    }
}
