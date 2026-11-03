using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.UseCases.Browsing.Selection;

/// <summary>
/// The items a bulk operation works on (E14-S03-T01): either explicit IDs, or all results up to the newest one when
/// Select all ran, with exceptions. Select all needs no loading, and items added afterwards stay out of it.
/// Resolve it into IDs with <see cref="ResolveItemSelectionQuery"/>.
/// </summary>
public sealed class ItemSelection
{
    private ItemSelection(Id<MediaItem>? newest, IReadOnlySet<Id<MediaItem>> ids, int count)
    {
        Newest = newest;
        Ids = ids;
        Count = count;
    }

    /// <summary>
    /// Gets an empty selection.
    /// </summary>
    public static ItemSelection None { get; } = new(null, new HashSet<Id<MediaItem>>(), 0);

    /// <summary>
    /// Gets a value indicating whether the selection is "all results except <see cref="Ids"/>".
    /// </summary>
    public bool IsAll => Newest is not null;

    /// <summary>
    /// Gets the newest result when Select all ran, or <see langword="null"/> for explicit IDs. Results up to it are in
    /// an all-results selection.
    /// </summary>
    public Id<MediaItem>? Newest { get; }

    /// <summary>
    /// Gets the selected IDs, or for an all-results selection the exceptions: results it covers that the user
    /// deselected, and newer results the user selected.
    /// </summary>
    public IReadOnlySet<Id<MediaItem>> Ids { get; }

    /// <summary>
    /// Gets the number of selected items, as last known.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets whether one or several items are selected.
    /// </summary>
    public SelectionKind Kind => KindOf(Count);

    /// <summary>
    /// Creates a selection of explicit items.
    /// </summary>
    /// <param name="ids">The items.</param>
    /// <returns>The selection.</returns>
    public static ItemSelection Of(IEnumerable<Id<MediaItem>> ids)
    {
        var set = ids.ToHashSet();
        return set.Count == 0 ? None : new ItemSelection(null, set, set.Count);
    }

    /// <summary>
    /// Creates an all-results selection.
    /// </summary>
    /// <param name="newest">The newest result when Select all ran.</param>
    /// <param name="exceptions">Results up to <paramref name="newest"/> that aren't selected, and newer ones that are.</param>
    /// <param name="count">The number of selected items.</param>
    /// <returns>The selection.</returns>
    public static ItemSelection All(Id<MediaItem> newest, IEnumerable<Id<MediaItem>> exceptions, int count) =>
        new(newest, exceptions.ToHashSet(), count);

    /// <summary>
    /// Gets the kind of selection that holds a number of items.
    /// </summary>
    /// <param name="count">The number of selected items.</param>
    /// <returns>The kind.</returns>
    public static SelectionKind KindOf(int count) => count switch
    {
        <= 0 => SelectionKind.None,
        1 => SelectionKind.Single,
        _ => SelectionKind.Multiple,
    };

    /// <summary>
    /// Tells whether an item was added no later than another. IDs are UUIDv7, so their order is the order items were
    /// added, and the library compares them the same way.
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="newest">The newest item.</param>
    /// <returns><see langword="true"/> if <paramref name="id"/> is <paramref name="newest"/> or older.</returns>
    public static bool IsUpTo(Id<MediaItem> id, Id<MediaItem> newest) => id.Value.CompareTo(newest.Value) <= 0;

    /// <summary>
    /// Tells whether an item is selected.
    /// </summary>
    /// <param name="id">The item.</param>
    /// <returns><see langword="true"/> if it's selected.</returns>
    public bool Contains(Id<MediaItem> id) => Newest is { } newest ? IsUpTo(id, newest) != Ids.Contains(id) : Ids.Contains(id);
}
