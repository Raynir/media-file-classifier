using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores what visual near-duplicate detection needs (E12-S02): the queue of items to check and the visual groups.
/// The database queues an item, in the same transaction as the change, when its perceptual hash, content hash,
/// Missing/Excluded status, whole-item embedding or keyframes change, and the group mates of a deleted item. The
/// matching itself runs outside the library, which then applies the result with <see cref="ApplyAsync"/>. Group
/// changes raise <see cref="IDuplicateGroupRepository.Changed"/>.
/// </summary>
public interface IVisualDuplicateRepository
{
    /// <summary>
    /// Reads up to <paramref name="limit"/> of the oldest queued items without taking them off the queue.
    /// </summary>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items; <see cref="VisualQueueBatch.IsEmpty"/> when nothing is queued.</returns>
    Task<VisualQueueBatch> PeekQueueAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the queued items.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items waiting to be checked.</returns>
    Task<int> CountQueuedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues every item with a perceptual hash or keyframes, and every visual group member, so working the queue off
    /// rebuilds all visual groups: the full pass (E12-S02-T04), e.g. after the sensitivity changed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items queued afterwards.</returns>
    Task<int> QueueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what matching needs of items that can be visual duplicates: they aren't Missing or Excluded, and have a
    /// perceptual hash or keyframe hashes.
    /// </summary>
    /// <param name="itemIds">The items to read, or <c>null</c> for every such item. Items that don't qualify are left out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The candidates, in item ID order.</returns>
    Task<IReadOnlyList<VisualCandidate>> GetCandidatesAsync(IReadOnlyCollection<Id<MediaItem>>? itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the members of every visual group that has one of the items.
    /// </summary>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each group's members.</returns>
    Task<IReadOnlyList<IReadOnlyList<Id<MediaItem>>>> GetGroupsOfAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the visual groups within a scope with the groups matching found, in one short write, and takes the
    /// checked items off the queue unless they were queued again meanwhile. Each new group keeps the ID and state of
    /// the old group it overlaps most; a Resolved or Ignored group that gains a member opens again; old groups that
    /// no new group takes are deleted. Members that are gone, Missing or Excluded by now are left out.
    /// </summary>
    /// <param name="update">The scope, its groups, and the queue entries checked.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed.</returns>
    Task<VisualReconcileResult> ApplyAsync(VisualGroupUpdate update, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// Queued items read by <see cref="IVisualDuplicateRepository.PeekQueueAsync"/>.
/// </summary>
/// <param name="Items">The items, oldest first.</param>
/// <param name="Sequences">The queue entries read. Queuing an item again replaces its entry with a new one, so taking
/// these entries off never drops a change made after they were read.</param>
public sealed record VisualQueueBatch(IReadOnlyList<Id<MediaItem>> Items, IReadOnlyList<long> Sequences)
{
    /// <summary>
    /// Gets an empty batch.
    /// </summary>
    public static VisualQueueBatch Empty { get; } = new([], []);

    /// <summary>
    /// Gets a value indicating whether nothing was queued.
    /// </summary>
    public bool IsEmpty => Sequences.Count == 0;
}

/// <summary>
/// What visual matching knows of an item.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="MediaType">Image, animated image or video.</param>
/// <param name="ContentHash">The full content hash, when computed: items that share it are exact copies.</param>
/// <param name="PerceptualHash">The perceptual hash of an image, or of an animated image's first frame.</param>
/// <param name="FrameHashes">The perceptual hashes of the item's keyframes, in keyframe order; empty for a still.</param>
/// <param name="Duration">The duration of a video or animated image, when known.</param>
public sealed record VisualCandidate(
    Id<MediaItem> ItemId,
    MediaType MediaType,
    UInt128? ContentHash,
    ulong? PerceptualHash,
    IReadOnlyList<ulong> FrameHashes,
    TimeSpan? Duration);

/// <summary>
/// The visual groups found within a scope, for <see cref="IVisualDuplicateRepository.ApplyAsync"/>.
/// </summary>
/// <param name="Scope">Every item matching looked at: the visual groups that have one of them are replaced.</param>
/// <param name="Groups">The groups found, each with two or more items of the scope.</param>
/// <param name="Checked">The queue entries this result covers.</param>
public sealed record VisualGroupUpdate(
    IReadOnlyCollection<Id<MediaItem>> Scope,
    IReadOnlyList<IReadOnlyCollection<Id<MediaItem>>> Groups,
    VisualQueueBatch Checked);

/// <summary>
/// What <see cref="IVisualDuplicateRepository.ApplyAsync"/> changed.
/// </summary>
/// <param name="Items">Queue entries taken off.</param>
/// <param name="Created">Groups created.</param>
/// <param name="Changed">Groups whose members changed.</param>
/// <param name="Reopened">Resolved or Ignored groups opened again by a new member.</param>
/// <param name="Deleted">Groups deleted.</param>
public sealed record VisualReconcileResult(int Items, int Created, int Changed, int Reopened, int Deleted)
{
    /// <summary>
    /// Gets a value indicating whether any group was created, changed or deleted.
    /// </summary>
    public bool ChangedGroups => Created + Changed + Deleted > 0;
}
