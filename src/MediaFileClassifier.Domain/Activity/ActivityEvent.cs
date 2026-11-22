using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Activity;

/// <summary>
/// One significant thing that happened in the library (E15-S04): an import, a rescan, a bulk edit and so on. The
/// event keeps the numbers and names, not a sentence, so the view can write it in the user's language. It points at the
/// folder or item it concerns without owning it: removing either leaves the event, and the view then finds the
/// link has nothing to open.
/// </summary>
public sealed class ActivityEvent
{
    private static readonly IReadOnlyDictionary<string, long> NoCounts = new Dictionary<string, long>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityEvent"/> class.
    /// </summary>
    /// <param name="id">The ID.</param>
    /// <param name="type">What happened.</param>
    /// <param name="severity">How it ended.</param>
    /// <param name="occurredAt">When it happened.</param>
    /// <param name="subject">What it happened to, such as a folder path, or <see langword="null"/>.</param>
    /// <param name="counts">Named numbers, see <see cref="ActivityCounts"/>.</param>
    /// <param name="folderId">The folder it concerns, or <see langword="null"/>.</param>
    /// <param name="itemId">The one item it concerns, or <see langword="null"/>.</param>
    public ActivityEvent(
        Id<ActivityEvent> id,
        ActivityEventType type,
        ActivitySeverity severity,
        DateTimeOffset occurredAt,
        string? subject,
        IReadOnlyDictionary<string, long>? counts,
        Id<IndexedFolder>? folderId,
        Id<MediaItem>? itemId)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        Id = id;
        Type = type;
        Severity = severity;
        OccurredAt = occurredAt;
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        Counts = counts is { Count: > 0 } ? new Dictionary<string, long>(counts) : NoCounts;
        FolderId = folderId;
        ItemId = itemId;
    }

    /// <summary>
    /// Gets the ID. IDs are time-ordered, so they break ties between events of the same millisecond.
    /// </summary>
    public Id<ActivityEvent> Id { get; }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public ActivityEventType Type { get; }

    /// <summary>
    /// Gets how it ended.
    /// </summary>
    public ActivitySeverity Severity { get; }

    /// <summary>
    /// Gets when it happened.
    /// </summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Gets what it happened to, such as a folder path, or <see langword="null"/>.
    /// </summary>
    public string? Subject { get; }

    /// <summary>
    /// Gets the named numbers of the event, see <see cref="ActivityCounts"/>.
    /// </summary>
    public IReadOnlyDictionary<string, long> Counts { get; }

    /// <summary>
    /// Gets the folder the event concerns, or <see langword="null"/>.
    /// </summary>
    public Id<IndexedFolder>? FolderId { get; }

    /// <summary>
    /// Gets the one item the event concerns, or <see langword="null"/>.
    /// </summary>
    public Id<MediaItem>? ItemId { get; }

    /// <summary>
    /// Creates a new event.
    /// </summary>
    /// <param name="type">What happened.</param>
    /// <param name="severity">How it ended.</param>
    /// <param name="now">When it happened.</param>
    /// <param name="subject">What it happened to, or <see langword="null"/>.</param>
    /// <param name="counts">Named numbers, or <see langword="null"/>.</param>
    /// <param name="folderId">The folder it concerns, or <see langword="null"/>.</param>
    /// <param name="itemId">The one item it concerns, or <see langword="null"/>.</param>
    /// <returns>The event.</returns>
    public static ActivityEvent Create(
        ActivityEventType type,
        ActivitySeverity severity,
        DateTimeOffset now,
        string? subject = null,
        IReadOnlyDictionary<string, long>? counts = null,
        Id<IndexedFolder>? folderId = null,
        Id<MediaItem>? itemId = null) =>
        new(Id<ActivityEvent>.New(), type, severity, now, subject, counts, folderId, itemId);

    /// <summary>
    /// Gets a count, or 0 when the event has none by that name.
    /// </summary>
    /// <param name="name">The name, see <see cref="ActivityCounts"/>.</param>
    /// <returns>The count.</returns>
    public long Count(string name) => Counts.GetValueOrDefault(name);
}
