using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.UseCases.Browsing.Selection;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the media browser's selection model (E14-S03-T01): Explorer-style gestures, Select all without loading,
/// and keeping the selection across refreshes.
/// </summary>
public sealed class SelectionModelTests
{
    [Fact]
    public async Task Click_SelectsOnlyThatItem_AndSetsFocusAndAnchor()
    {
        var (selection, source) = Create(10);
        await selection.ApplyAsync(2, SelectionGesture.Replace);

        await selection.ApplyAsync(5, SelectionGesture.Replace);

        Assert.Equal([source.Ids[5]], Selected(selection, source));
        Assert.Equal((5, 5, source.Ids[5]), (selection.FocusIndex, selection.AnchorIndex, selection.FocusedId));
        Assert.Equal(SelectionKind.Single, selection.Kind);
    }

    [Fact]
    public async Task CtrlClick_TogglesAndMovesTheAnchor()
    {
        var (selection, source) = Create(10);
        await selection.ApplyAsync(1, SelectionGesture.Replace);

        await selection.ApplyAsync(4, SelectionGesture.Toggle);
        await selection.ApplyAsync(1, SelectionGesture.Toggle);

        Assert.Equal([source.Ids[4]], Selected(selection, source));
        Assert.Equal(1, selection.AnchorIndex);
    }

    [Fact]
    public async Task ShiftClick_SelectsFromTheAnchor_ReplacingTheSelection_AndCtrlShiftAdds()
    {
        var (selection, source) = Create(20);
        await selection.ApplyAsync(15, SelectionGesture.Replace);
        await selection.ApplyAsync(5, SelectionGesture.Toggle);

        await selection.ApplyAsync(8, SelectionGesture.Range);
        Assert.Equal(source.Ids.Skip(5).Take(4), Selected(selection, source));

        // Shift again from the same anchor replaces the range, going the other way.
        await selection.ApplyAsync(3, SelectionGesture.Range);
        Assert.Equal(source.Ids.Skip(3).Take(3), Selected(selection, source));

        await selection.ApplyAsync(12, SelectionGesture.Toggle);
        await selection.ApplyAsync(14, SelectionGesture.AddRange);
        Assert.Equal([.. source.Ids.Skip(3).Take(3), .. source.Ids.Skip(12).Take(3)], Selected(selection, source));
        Assert.Equal((14, 12), (selection.FocusIndex, selection.AnchorIndex));
        Assert.Equal(SelectionKind.Multiple, selection.Kind);
    }

    [Fact]
    public async Task FocusOnly_MovesTheFocusWithoutSelecting()
    {
        var (selection, source) = Create(10);
        await selection.ApplyAsync(1, SelectionGesture.Replace);

        await selection.ApplyAsync(3, SelectionGesture.FocusOnly);

        Assert.Equal([source.Ids[1]], Selected(selection, source));
        Assert.Equal((3, 1), (selection.FocusIndex, selection.AnchorIndex));
    }

    [Fact]
    public async Task Range_OverResultsNotLoaded_AsksTheSourceForTheirIdsOnce()
    {
        var (selection, source) = Create(100_000, known: false);
        await selection.ApplyAsync(10, SelectionGesture.Replace);
        var requests = source.IdRequests;

        await selection.ApplyAsync(90_000, SelectionGesture.Range);

        Assert.Equal(89_991, selection.Count);
        Assert.Equal(requests + 1, source.IdRequests);
        Assert.Equal(0, source.PageRequests);
    }

    [Fact]
    public async Task SelectAll_On100kResults_LoadsNothing_AndBulkOperationsGetEveryResult()
    {
        var (selection, source) = Create(100_000);

        await selection.SelectAllAsync();
        await selection.ApplyAsync(99_999, SelectionGesture.Toggle);

        // One ID is read: the newest item, the boundary in any order (E10-S04).
        Assert.Equal(0, source.PageRequests);
        Assert.Equal(1, source.IdRequests);
        Assert.Equal(99_999, selection.Count);
        Assert.True(selection.IsAll);
        Assert.False(selection.IsSelected(source.Ids[99_999]));
        Assert.True(selection.IsSelected(source.Ids[50_000]));
        var snapshot = selection.Snapshot();
        Assert.Equal((true, 99_999), (snapshot.IsAll, snapshot.Count));
        Assert.Equal([source.Ids[99_999]], snapshot.Ids);
        Assert.Equal(source.Ids[0], snapshot.Newest);
    }

    [Fact]
    public async Task SelectAll_LeavesOutItemsAddedLater_AndToggledNewItemsCount()
    {
        var (selection, source) = Create(10);
        await selection.SelectAllAsync();

        source.InsertAtTop(3);
        await selection.RefreshAsync(13);

        Assert.Equal(10, selection.Count);
        Assert.False(selection.IsSelected(source.Ids[0]));
        Assert.True(selection.IsSelected(source.Ids[3]));

        await selection.ApplyAsync(0, SelectionGesture.Toggle);
        Assert.Equal(11, selection.Count);
        Assert.True(selection.Snapshot().Contains(source.Ids[0]));
    }

    [Fact]
    public async Task Refresh_KeepsItemsStillThere_AndTheFocusFollowsItsItem()
    {
        var (selection, source) = Create(10);
        await selection.ApplyAsync(2, SelectionGesture.Replace);
        await selection.ApplyAsync(6, SelectionGesture.Range);
        var focused = source.Ids[6];
        var removed = source.Ids[3];

        source.Remove(removed);
        source.InsertAtTop(2);
        await selection.RefreshAsync(11);

        Assert.Equal(4, selection.Count);
        Assert.False(selection.IsSelected(removed));
        Assert.Equal((7, focused), (selection.FocusIndex, selection.FocusedId));
        Assert.Equal(4, selection.AnchorIndex);
    }

    [Fact]
    public async Task Refresh_WhenTheFocusedItemIsGone_KeepsThePositionWithinTheResults()
    {
        var (selection, source) = Create(5);
        await selection.ApplyAsync(4, SelectionGesture.Replace);

        source.Remove(source.Ids[4], source.Ids[3]);
        await selection.RefreshAsync(3);

        Assert.Equal((0, 2), (selection.Count, selection.FocusIndex));
    }

    [Fact]
    public async Task ClearAndReset_DeselectEverything()
    {
        var (selection, _) = Create(10);
        await selection.SelectAllAsync();
        await selection.ApplyAsync(3, SelectionGesture.FocusOnly);

        selection.Clear();
        Assert.Equal((0, false, 3), (selection.Count, selection.IsAll, selection.FocusIndex));

        selection.Reset(0);
        Assert.Equal((-1, -1), (selection.FocusIndex, selection.AnchorIndex));
        Assert.Equal(SelectionKind.None, selection.Kind);
    }

    [Fact]
    public async Task CtrlShiftClick_WithoutAnAnchor_AddsToTheSelection()
    {
        var (selection, source) = Create(10);
        await selection.SelectAllAsync();

        await selection.ApplyAsync(4, SelectionGesture.AddRange);

        Assert.Equal((10, 4), (selection.Count, selection.AnchorIndex));
        Assert.True(selection.IsSelected(source.Ids[0]));
    }

    [Fact]
    public async Task Refresh_DoesNotUndoAGestureMadeWhileItRuns_NorCancelIt()
    {
        var (selection, source) = Create(10);
        await selection.ApplyAsync(2, SelectionGesture.Replace);
        var gate = new TaskCompletionSource();
        source.ExistingGate = gate.Task;

        var refresh = selection.RefreshAsync(10);
        await selection.ApplyAsync(5, SelectionGesture.Toggle);
        gate.SetResult();
        await refresh;

        Assert.Equal([source.Ids[2], source.Ids[5]], Selected(selection, source));
        Assert.Equal((5, 5), (selection.FocusIndex, selection.AnchorIndex));
    }

    [Fact]
    public async Task StatusSegment_ShowsTheSelectedCount_OnlyWhileSomethingIsSelected()
    {
        var (selection, _) = Create(2_000);
        var statusBar = new StatusBarViewModel([new SelectionStatusSegment(selection, Localizer.Current)]);
        var segment = Assert.Single(statusBar.Left);
        Assert.False(segment.IsVisible);

        await selection.SelectAllAsync();

        Assert.True(segment.IsVisible);
        Assert.Equal(SelectionStatusSegment.SegmentId, segment.Id);
        Assert.Equal($"{Format.Number(2_000)} selected", segment.Text);
        selection.Clear();
        Assert.False(segment.IsVisible);
    }

    [Fact]
    public async Task Gestures_OutsideTheResults_DoNothing()
    {
        var (selection, _) = Create(3);
        var changes = 0;
        selection.Changed += (_, _) => changes++;

        await selection.ApplyAsync(3, SelectionGesture.Replace);
        await selection.ApplyAsync(-1, SelectionGesture.Toggle);

        Assert.Equal((0, -1, 0), (selection.Count, selection.FocusIndex, changes));
    }

    [Theory]
    [InlineData(false, 7, 2)]
    [InlineData(true, 2, 0)]
    public async Task Gesture_AsksTheGuardFirst_AndOnlyChangesTheSelectionWhenItAllows(bool refuse, int expectedFocus, int expectedAsks)
    {
        var (selection, _) = Create(10);
        await selection.ApplyAsync(2, SelectionGesture.Replace);
        var asked = 0;
        selection.ConfirmChange = () =>
        {
            asked++;
            return Task.FromResult(!refuse);
        };

        if (refuse)
        {
            await selection.ApplyAsync(5, SelectionGesture.Replace);
            Assert.Equal(1, asked);
            asked--;
        }
        else
        {
            await selection.ApplyAsync(5, SelectionGesture.Replace);
            await selection.ApplyAsync(7, SelectionGesture.Replace);
        }

        Assert.Equal(expectedFocus, selection.FocusIndex);
        Assert.Equal(expectedAsks, asked);
    }

    [Fact]
    public async Task ClickingTheOnlySelectedItemAgain_AsksNothing()
    {
        var (selection, _) = Create(10);
        await selection.ApplyAsync(2, SelectionGesture.Replace);
        selection.ConfirmChange = () => throw new InvalidOperationException("No prompt expected.");

        await selection.ApplyAsync(2, SelectionGesture.Replace);

        Assert.Equal(2, selection.FocusIndex);
    }

    [Fact]
    public async Task SelectAll_AsksTheGuardFirst()
    {
        var (selection, _) = Create(10);
        selection.ConfirmChange = () => Task.FromResult(false);

        await selection.SelectAllAsync();

        Assert.Equal(0, selection.Count);
    }

    private static (SelectionModel Selection, FakeResultSource Source) Create(int count, bool known = true)
    {
        var source = new FakeResultSource(count);
        var selection = new SelectionModel(() => source);
        if (known)
        {
            selection.KnownId = index => index >= 0 && index < source.Ids.Count ? source.Ids[index] : null;
        }

        selection.Reset(count);
        return (selection, source);
    }

    private static List<Domain.Identity.Id<Domain.Media.MediaItem>> Selected(SelectionModel selection, FakeResultSource source) =>
        [.. source.Ids.Where(selection.IsSelected)];
}
