using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;

namespace MediaFileClassifier.Infrastructure.Tests.Metadata;

/// <summary>
/// Tests for bulk writes and their undo (E07-S03-T02, T03) through <see cref="EditMetadataCommand"/> and
/// <see cref="UndoBulkEditCommand"/> against a real library.
/// </summary>
public sealed class BulkEditFlowTests : IAsyncLifetime
{
    private MetadataTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await MetadataTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Undo_PutsBackFieldsTagsTombstonesAndAiValueState_OfEveryChangedItem_AndSearchFollows()
    {
        var second = await bed.AddItemAsync(2);
        var third = await bed.AddItemAsync(3);
        List<MediaItem> items = [bed.Item, second, third];
        foreach (var item in items)
        {
            await bed.ApplyRunAsync("Dog", ["dog", "sea"], item);
        }

        await Edit([.. items.Select(item => item.Id)], new MetadataEdit.SetField(DescriptiveField.Notes, FieldValue.FromText("Italy")));
        await Edit([bed.Item.Id, second.Id], new MetadataEdit.ConfirmField(DescriptiveField.Title), new MetadataEdit.AddTag("holiday"));
        var before = await SnapshotsAsync(items);

        var result = await bed.Sender.Send(
            new EditMetadataCommand(
                [.. items.Select(item => item.Id)],
                [
                    new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("Trip")),
                    new MetadataEdit.RevertField(DescriptiveField.Notes),
                    new MetadataEdit.RemoveTag(TagName.From("holiday")),
                    new MetadataEdit.RejectTag(TagName.From("dog")),
                    new MetadataEdit.AddTag("summer"),
                ],
                CaptureUndo: true),
            Token);
        Assert.Equal(3, result.ChangedItemIds.Count);
        Assert.Equal(3, bed.Undo.Count);
        Assert.Contains(bed.Item.Id, await FoundAsync("summer"));

        var undone = await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        Assert.Equal(3, undone.RestoredItemIds.Count);
        var after = await SnapshotsAsync(items);
        for (var index = 0; index < items.Count; index++)
        {
            Assert.Equivalent(before[index], after[index]);
        }

        Assert.Empty(await FoundAsync("summer"));
        Assert.Equal(new HashSet<Id<MediaItem>> { bed.Item.Id, second.Id }, (await FoundAsync("holiday")).ToHashSet());
        Assert.Equal(0, bed.Undo.Count);
    }

    [Fact]
    public async Task Undo_ASecondTime_OrWithoutABulkEdit_RestoresNothing()
    {
        var empty = await bed.Sender.Send(new UndoBulkEditCommand(), Token);
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id], [new MetadataEdit.AddTag("trip")], CaptureUndo: true), Token);
        var first = await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        var second = await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        Assert.Empty(empty.RestoredItemIds);
        Assert.Single(first.RestoredItemIds);
        Assert.Empty(second.RestoredItemIds);
    }

    [Fact]
    public async Task Undo_OnlyKnowsTheLastBulkEdit_AndOverwritesWhatWasEditedSince()
    {
        var second = await bed.AddItemAsync(2);
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id], [new MetadataEdit.AddTag("one")], CaptureUndo: true), Token);
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id, second.Id], [new MetadataEdit.AddTag("two")], CaptureUndo: true), Token);
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id], [new MetadataEdit.AddTag("plain")]), Token);

        await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        var tags = (await bed.GetAsync()).GetEffectiveTags().Select(tag => tag.Tag.Value).Order();
        Assert.Equal(["one"], tags);
        Assert.Empty((await bed.GetAsync(second)).GetEffectiveTags());
    }

    [Fact]
    public async Task Undo_IsDropped_WhenAnotherLibraryIsOpened()
    {
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id], [new MetadataEdit.AddTag("trip")], CaptureUndo: true), Token);

        await bed.Session.SetCurrentAsync(null);

        Assert.Equal(0, bed.Undo.Count);
    }

    [Fact]
    public async Task Undo_PublishesOneChangeWithEveryRestoredItem()
    {
        var second = await bed.AddItemAsync(2);
        await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id, second.Id], [new MetadataEdit.AddTag("trip")], CaptureUndo: true), Token);
        bed.Published.Clear();

        await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        var change = Assert.Single(bed.Published);
        Assert.Equal(new HashSet<Id<MediaItem>> { bed.Item.Id, second.Id }, change.ItemIds.ToHashSet());
        Assert.True(change.Change.HasFlag(ItemMetadataChange.Tags));
    }

    [Fact]
    public async Task Edit_ReportsProgressForEveryItem_AndKeepsUndoOnlyForTheOnesThatChanged()
    {
        var items = await AddItemsAsync(250);
        await Edit([items[0]], new MetadataEdit.AddTag("trip"));
        var reported = new List<int>();

        var result = await bed.Sender.Send(
            new EditMetadataCommand(items, [new MetadataEdit.AddTag("trip")], CaptureUndo: true, new SyncProgress(reported.Add)),
            Token);

        Assert.Equal(249, result.ChangedItemIds.Count);
        Assert.Equal(249, bed.Undo.Count);
        Assert.Equal(Enumerable.Range(1, 250), reported);
    }

    [Fact]
    public async Task Edit_StoppedAfterABatch_KeepsTheBatchesWritten_AndUndoPutsThemBack()
    {
        var items = await AddItemsAsync(350);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bed.Sender.Send(
            new EditMetadataCommand(items, [new MetadataEdit.AddTag("trip")], CaptureUndo: true, new SyncProgress(count =>
            {
                if (count == 150)
                {
                    cancel.Cancel();
                }
            })),
            cancel.Token));

        var written = (await FoundAsync("trip")).Count;
        Assert.InRange(written, 100, 349);
        Assert.Equal(written, bed.Undo.Count);

        var undone = await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        Assert.Equal(written, undone.RestoredItemIds.Count);
        Assert.Empty(await FoundAsync("trip"));
    }

    [Fact]
    public async Task Edit_RefusedValues_StoreNoUndo()
    {
        var result = await bed.Sender.Send(
            new EditMetadataCommand([bed.Item.Id], [new MetadataEdit.AddTag(new string('x', 500))], CaptureUndo: true),
            Token);

        Assert.NotNull(result.Error);
        Assert.Equal(0, bed.Undo.Count);
    }

    [Fact]
    public async Task TaggingTenThousandItems_IsWrittenInBatches_AndUndone_AndTheMeasurementIsReported()
    {
        var items = await AddItemsAsync(10_000);
        var start = Stopwatch.StartNew();
        var maxHandled = 0;

        var result = await bed.Sender.Send(
            new EditMetadataCommand(items, [new MetadataEdit.AddTag("trip")], CaptureUndo: true, new SyncProgress(count => maxHandled = count)),
            Token);
        var tagged = start.Elapsed;
        var undone = await bed.Sender.Send(new UndoBulkEditCommand(), Token);
        TestContext.Current.SendDiagnosticMessage(
            $"10k items: tagging {tagged.TotalSeconds:F1} s, undo {(start.Elapsed - tagged).TotalSeconds:F1} s, published {bed.Published.Count} notification(s)");

        Assert.Equal(10_000, result.ChangedItemIds.Count);
        Assert.Equal(10_000, maxHandled);
        Assert.Equal(10_000, undone.RestoredItemIds.Count);
    }

    private async Task<List<Id<MediaItem>>> AddItemsAsync(int count)
    {
        var ids = new List<Id<MediaItem>> { bed.Item.Id };
        var batch = new List<MediaItem>();
        for (var number = 2; number <= count; number++)
        {
            batch.Add(bed.Library.NewItem(number));
        }

        await bed.Open.Items.AddRangeAsync(batch, Token);
        ids.AddRange(batch.Select(item => item.Id));
        return ids;
    }

    private async Task<List<UserLayerSnapshot>> SnapshotsAsync(IEnumerable<MediaItem> items)
    {
        var snapshots = new List<UserLayerSnapshot>();
        foreach (var item in items)
        {
            snapshots.Add((await bed.GetAsync(item)).CaptureUserLayer());
        }

        return snapshots;
    }

    private Task Edit(IReadOnlyCollection<Id<MediaItem>> ids, params MetadataEdit[] edits) =>
        bed.Sender.Send(new EditMetadataCommand(ids, edits), Token);

    private async Task<IReadOnlyList<Id<MediaItem>>> FoundAsync(string word) =>
        [.. (await bed.Open.Search.FindKeywordMatchesAsync(SearchStoreTests.Search(word) with { Limit = 20_000 }, Token)).Matches.Select(match => match.ItemId)];

    // Reports on the calling thread, so a test sees every value in order.
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
