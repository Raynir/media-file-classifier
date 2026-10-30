using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// One tag on one item, with its source. Tags are stored as individual assignments, so each keeps its own provenance.
/// </summary>
/// <remarks>
/// Invariants: an AI assignment belongs to a run and records its model; a user assignment records its origin,
/// and an accepted one references the run it was accepted from; a technical assignment has neither.
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
    public TagAssignment(
        Id<MediaItem> itemId,
        TagName tag,
        MetadataSource source,
        Id<AnalysisRun>? runId,
        Id<ModelRegistration>? modelId,
        double? confidence,
        UserValueOrigin? origin,
        RunReference? acceptedFrom,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(tag);
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
    }

    public Id<MediaItem> ItemId { get; }

    public TagName Tag { get; }

    public MetadataSource Source { get; }

    public Id<AnalysisRun>? RunId { get; }

    public Id<ModelRegistration>? ModelId { get; }

    public double? Confidence { get; }

    public UserValueOrigin? Origin { get; }

    public RunReference? AcceptedFrom { get; }

    public DateTimeOffset CreatedAt { get; }

    public static TagAssignment Technical(Id<MediaItem> itemId, TagName tag, DateTimeOffset now) =>
        new(itemId, tag, MetadataSource.Technical, null, null, null, null, null, now);

    public static TagAssignment Ai(AnalysisRun run, TagName tag, double? confidence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new(run.ItemId, tag, MetadataSource.AI, run.Id, run.ModelId, confidence, null, null, now);
    }

    public static TagAssignment UserEdited(Id<MediaItem> itemId, TagName tag, DateTimeOffset now) =>
        new(itemId, tag, MetadataSource.User, null, null, null, UserValueOrigin.Edited, null, now);

    public static TagAssignment UserAccepted(Id<MediaItem> itemId, TagName tag, RunReference run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new(itemId, tag, MetadataSource.User, null, null, null, UserValueOrigin.Accepted, run, now);
    }
}
