using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.Selection;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// What a click or key does to the selection (E14-S03-T02), following Windows Explorer.
/// </summary>
public enum SelectionGesture
{
    /// <summary>Select only the item, e.g. a click or an arrow key.</summary>
    Replace,

    /// <summary>Toggle the item, e.g. Ctrl+click or Ctrl+Space.</summary>
    Toggle,

    /// <summary>Select from the anchor to the item instead of the selection, e.g. Shift+click or Shift+arrow.</summary>
    Range,

    /// <summary>Add the anchor-to-item range to the selection, e.g. Ctrl+Shift+click.</summary>
    AddRange,

    /// <summary>Move the focus only, e.g. Ctrl+arrow.</summary>
    FocusOnly,
}

/// <summary>
/// The media browser's selection, focus and anchor (E14-S03-T01). It holds explicit IDs, or "all results up to the
/// newest one, except these" after Select all, so selecting 100k results loads nothing. Focus and anchor are result
/// positions with their item IDs, so they survive scrolling and refreshes. The grid and, later, the list view
/// (E14-S02) share it. Use it on the UI thread.
/// </summary>
/// <param name="results">Gets the current results, once a gesture needs them.</param>
public sealed partial class SelectionModel(Func<IMediaResultSource> results) : ObservableObject
{
    // Selected IDs, or with Select all the exceptions to it (see ItemSelection).
    private readonly HashSet<Id<MediaItem>> ids = [];
    private Id<MediaItem>? newest;
    private Id<MediaItem>? focusedId;
    private Id<MediaItem>? anchorId;
    private int version;
    private int resets;

    /// <summary>
    /// Raised when the selection or the focus changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the number of selected items.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind))]
    public partial int Count { get; private set; }

    /// <summary>
    /// Gets whether one or several items are selected, so the details panel can pick its mode (E07-S03).
    /// </summary>
    public SelectionKind Kind => ItemSelection.KindOf(Count);

    /// <summary>
    /// Gets a value indicating whether Select all made the selection.
    /// </summary>
    public bool IsAll => newest is not null;

    /// <summary>
    /// Gets the focused result position, or -1 when nothing has focus.
    /// </summary>
    public int FocusIndex { get; private set; } = -1;

    /// <summary>
    /// Gets the focused item, or <see langword="null"/> when it isn't known.
    /// </summary>
    public Id<MediaItem>? FocusedId => focusedId ?? (FocusIndex >= 0 ? KnownId?.Invoke(FocusIndex) : null);

    /// <summary>
    /// Gets the position ranges start from, or -1.
    /// </summary>
    public int AnchorIndex { get; private set; } = -1;

    /// <summary>
    /// Gets the number of results.
    /// </summary>
    public int ResultCount { get; private set; }

    /// <summary>
    /// Gets or sets how to look up the IDs of results that are already loaded, so most gestures need no query.
    /// </summary>
    public Func<int, Id<MediaItem>?>? KnownId { get; set; }

    /// <summary>
    /// Gets or sets a check made before a gesture changes the selection (the unsaved changes guard, E07-S12-T01). It
    /// returns <see langword="false"/> to keep the selection as it is.
    /// </summary>
    public Func<Task<bool>>? ConfirmChange { get; set; }

    /// <summary>
    /// Tells whether an item is selected.
    /// </summary>
    /// <param name="id">The item.</param>
    /// <returns><see langword="true"/> if it's selected.</returns>
    public bool IsSelected(Id<MediaItem> id) =>
        newest is { } boundary ? ItemSelection.IsUpTo(id, boundary) != ids.Contains(id) : ids.Contains(id);

    /// <summary>
    /// Gets the selection as a value for bulk operations.
    /// </summary>
    /// <returns>The selection.</returns>
    public ItemSelection Snapshot() => newest is { } boundary ? ItemSelection.All(boundary, ids, Count) : ItemSelection.Of(ids);

    /// <summary>
    /// Applies a click or key to a result.
    /// </summary>
    /// <param name="index">The result position.</param>
    /// <param name="gesture">What to do.</param>
    /// <returns>A task that completes when the selection changed.</returns>
    public async Task ApplyAsync(int index, SelectionGesture gesture)
    {
        if (index < 0 || index >= ResultCount)
        {
            return;
        }

        var stamp = ++version;
        if ((gesture is SelectionGesture.Range or SelectionGesture.AddRange) && AnchorIndex >= 0)
        {
            var first = Math.Min(AnchorIndex, index);
            var range = await IdsAsync(first, Math.Max(AnchorIndex, index) - first + 1);
            if (stamp != version || !await AllowAsync(stamp))
            {
                return;
            }

            if (gesture == SelectionGesture.Range)
            {
                ClearIds();
            }

            foreach (var id in range)
            {
                Set(id, selected: true);
            }

            SetFocus(index, index - first < range.Count ? range[index - first] : null);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var item = await IdAsync(index);
        if (stamp != version || item is not { } focused)
        {
            return;
        }

        // Clicking the only selected item again changes nothing, so it needs no prompt.
        var unchanged = gesture == SelectionGesture.Replace && Count == 1 && focused == FocusedId && IsSelected(focused);
        if (!unchanged && !await AllowAsync(stamp))
        {
            return;
        }

        switch (gesture)
        {
            case SelectionGesture.Toggle:
                Set(focused, !IsSelected(focused));
                break;
            case SelectionGesture.AddRange:
                // Ctrl+Shift with no anchor yet adds the item, keeping the selection.
                Set(focused, selected: true);
                break;
            case SelectionGesture.FocusOnly:
                SetFocus(index, focused);
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            default:
                ClearIds();
                Set(focused, selected: true);
                break;
        }

        SetFocus(index, focused);
        AnchorIndex = index;
        anchorId = focused;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects all results without loading them. Results added later aren't selected.
    /// </summary>
    /// <returns>A task that completes when the selection changed.</returns>
    public async Task SelectAllAsync()
    {
        if (ResultCount == 0)
        {
            return;
        }

        var stamp = ++version;

        // The boundary is the newest item that can be a result, not result 0: in another order than newest import
        // (E10-S04) result 0 can be any item. Ask the source for both, so the count matches the boundary even before
        // the grid's next refresh.
        var newestTask = results().GetNewestIdAsync(CancellationToken.None);
        var countTask = results().CountAsync(CancellationToken.None);
        var first = await newestTask;
        var count = await countTask;

        if (stamp != version || first is not { } boundary || !await AllowAsync(stamp))
        {
            return;
        }

        ids.Clear();
        newest = boundary;
        Count = count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deselects everything, keeping the focus.
    /// </summary>
    public void Clear()
    {
        version++;
        if (Count == 0 && ids.Count == 0 && newest is null)
        {
            return;
        }

        ClearIds();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets the selection, focus and results, e.g. when the library closes or the results are replaced.
    /// </summary>
    /// <param name="resultCount">The number of new results.</param>
    public void Reset(int resultCount)
    {
        version++;
        resets++;
        ClearIds();
        ResultCount = resultCount;
        FocusIndex = AnchorIndex = -1;
        focusedId = anchorId = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Brings the selection up to date after the results changed: items no longer in the results drop out, and
    /// the focus and anchor follow their items to their new positions.
    /// </summary>
    /// <param name="resultCount">The number of results now.</param>
    /// <returns>A task that completes when the selection is up to date.</returns>
    public async Task RefreshAsync(int resultCount)
    {
        // Gestures made while this awaits stay: only what was checked is updated, and only if no gesture moved it.
        ResultCount = resultCount;
        var generation = resets;
        var boundary = newest;
        var (focusIndex, focusId) = (FocusIndex, focusedId);
        var (anchorIndex, anchorItem) = (AnchorIndex, anchorId);
        HashSet<Id<MediaItem>> check = [.. ids];
        if (focusId is { } focusCheck)
        {
            check.Add(focusCheck);
        }

        if (anchorItem is { } anchorCheck)
        {
            check.Add(anchorCheck);
        }

        var existing = check.Count == 0
            ? new HashSet<Id<MediaItem>>()
            : await results().GetExistingIdsAsync(check, CancellationToken.None);
        var newerTask = boundary is { } all ? results().CountNewerAsync(all, CancellationToken.None) : Task.FromResult(0);
        var focusTask = PositionAsync(focusId, focusIndex, existing, resultCount);
        var anchorTask = PositionAsync(anchorItem, anchorIndex, existing, resultCount);
        await Task.WhenAll(newerTask, focusTask, anchorTask);
        if (generation != resets)
        {
            return;
        }

        foreach (var id in check.Where(id => !existing.Contains(id)))
        {
            ids.Remove(id);
        }

        if (newest is { } current && current == boundary)
        {
            // Results up to the boundary, minus the ones deselected, plus newer ones selected.
            var added = ids.Count(id => !ItemSelection.IsUpTo(id, current));
            Count = Math.Max(0, resultCount - newerTask.Result - (ids.Count - added)) + added;
        }
        else if (newest is null)
        {
            Count = ids.Count;
        }

        if ((FocusIndex, focusedId) == (focusIndex, focusId))
        {
            (FocusIndex, focusedId) = focusTask.Result;
        }

        if ((AnchorIndex, anchorId) == (anchorIndex, anchorItem))
        {
            (AnchorIndex, anchorId) = anchorTask.Result;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finds where an item moved to. When it's gone, the position stays, within the results, so the focus lands on
    /// the item that took its place.
    /// </summary>
    private async Task<(int Index, Id<MediaItem>? Id)> PositionAsync(
        Id<MediaItem>? id, int index, IReadOnlySet<Id<MediaItem>> existing, int resultCount)
    {
        if (index < 0)
        {
            return (-1, null);
        }

        if (id is { } item && existing.Contains(item))
        {
            return (await results().PositionOfAsync(item, CancellationToken.None), item);
        }

        return resultCount == 0 ? (-1, null) : (Math.Min(index, resultCount - 1), null);
    }

    // Asks the guard before the selection changes. Another gesture made while the prompt was open wins, as it would
    // have without the prompt.
    private async ValueTask<bool> AllowAsync(int stamp)
    {
        if (ConfirmChange is { } confirm && !await confirm())
        {
            return false;
        }

        return stamp == version;
    }

    private void Set(Id<MediaItem> id, bool selected)
    {
        if (IsSelected(id) == selected)
        {
            return;
        }

        if (newest is null && selected)
        {
            ids.Add(id);
        }
        else if (newest is null || !ids.Add(id))
        {
            ids.Remove(id);
        }

        Count += selected ? 1 : -1;
    }

    private void ClearIds()
    {
        ids.Clear();
        newest = null;
        Count = 0;
    }

    private void SetFocus(int index, Id<MediaItem>? id)
    {
        FocusIndex = index;
        focusedId = id;
    }

    private async ValueTask<Id<MediaItem>?> IdAsync(int index)
    {
        if (KnownId?.Invoke(index) is { } known)
        {
            return known;
        }

        var found = await results().GetIdsAsync(index, 1, null, CancellationToken.None);
        return found.Count > 0 ? found[0] : null;
    }

    private async ValueTask<IReadOnlyList<Id<MediaItem>>> IdsAsync(int first, int count)
    {
        if (KnownId is { } known)
        {
            var list = new Id<MediaItem>[count];
            var offset = 0;
            for (; offset < count && known(first + offset) is { } id; offset++)
            {
                list[offset] = id;
            }

            if (offset == count)
            {
                return list;
            }
        }

        return await results().GetIdsAsync(first, count, null, CancellationToken.None);
    }
}
