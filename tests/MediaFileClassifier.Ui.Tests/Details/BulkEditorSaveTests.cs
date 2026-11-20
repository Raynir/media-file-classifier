using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for saving and undoing in the bulk editor (E07-S03-T02, T03): the confirmation, the progress and Stop, the
/// refused and failed saves, and the Undo of the last bulk edit.
/// </summary>
public sealed class BulkEditorSaveTests
{
    private readonly SavingSender sender = new();

    [Fact]
    public async Task RequestSave_OverTwentyItems_AsksFirst_AndTheNextRequestSavesAndShowsWhatChanged()
    {
        var (editor, _) = await LoadedAsync(120);
        editor.Fields[1].Text = "Rome";

        await editor.RequestSaveAsync();

        Assert.True(editor.IsConfirming);
        Assert.Equal("120 items will change.", editor.ConfirmText);
        Assert.Empty(sender.Edits);

        await editor.RequestSaveAsync();

        var command = Assert.Single(sender.Edits);
        Assert.Equal(120, command.ItemIds.Count);
        Assert.True(command.CaptureUndo);
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Description, FieldValue.FromText("Rome"))], command.Edits);
        Assert.Equal("120 items changed.", editor.ResultMessage);
        Assert.False(editor.IsConfirming);
        Assert.False(editor.IsDirty);
        Assert.False(editor.IsSaving);
        Assert.Equal(2, sender.Queries.Count);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task RequestSave_AsksOnlyAboveTwentyItems(int count, bool asks)
    {
        var (editor, _) = await LoadedAsync(count);
        editor.Fields[1].Text = "Rome";

        await editor.RequestSaveAsync();

        Assert.Equal(asks, editor.IsConfirming);
        Assert.Equal(asks ? 0 : 1, sender.Edits.Count);
    }

    [Fact]
    public async Task ChangingAFieldAfterTheQuestion_WithdrawsIt()
    {
        var (editor, _) = await LoadedAsync(120);
        editor.Fields[1].Text = "Rome";
        await editor.RequestSaveAsync();

        editor.Fields[0].Text = "Other";

        Assert.False(editor.IsConfirming);
        Assert.Empty(sender.Edits);
    }

    [Fact]
    public async Task Save_OfALargeSelection_ShowsProgress_AndStopKeepsWhatWasWritten_WithUndo()
    {
        var (editor, store) = await LoadedAsync(600);
        editor.Fields[1].Text = "Rome";
        sender.Block = true;
        sender.HandledBeforeBlocking = 250;
        sender.WrittenBeforeBlocking = 200;

        var saving = editor.SaveAsync();
        await sender.Started.Task;

        Assert.True(editor.IsSaving);
        Assert.True(editor.IsLargeSave);
        Assert.NotNull(editor.SaveProgressText);

        editor.CancelSaveCommand.Execute(null);
        await saving;

        Assert.False(editor.IsSaving);
        Assert.Equal("Stopped after 250 of 600 items. 200 items were changed.", editor.ResultMessage);
        Assert.True(editor.CanUndo);
        Assert.Equal(200, store.Count);
    }

    [Fact]
    public async Task Save_AValueTheServiceRefuses_ShowsTheReason_AndKeepsTheStagedChanges()
    {
        var (editor, _) = await LoadedAsync(5);
        editor.AddTag("trip");
        sender.Error = new MetadataValidationError(MetadataValidationCode.TooManyTags, Limit: 200);

        await editor.SaveAsync();

        Assert.NotNull(editor.TagError);
        Assert.True(editor.IsDirty);
        Assert.Null(editor.ResultMessage);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task Save_ThatFails_ShowsAMessage_AndKeepsTheStagedChanges()
    {
        var (editor, _) = await LoadedAsync(5);
        editor.Fields[1].Text = "Rome";
        sender.Failure = new InvalidOperationException("disk");

        await editor.SaveAsync();

        Assert.Equal("Couldn't save the changes. Try again.", editor.SaveError);
        Assert.True(editor.IsDirty);
        Assert.False(editor.IsSaving);

        editor.Cancel();

        Assert.Null(editor.SaveError);
    }

    [Fact]
    public async Task Undo_PutsTheItemsBack_ShowsHowMany_AndCanBeUsedOnce()
    {
        var (editor, store) = await LoadedAsync(30);
        editor.Fields[1].Text = "Rome";
        sender.WrittenWhenDone = 30;
        await editor.SaveAsync();
        Assert.True(editor.CanUndo);

        await editor.UndoAsync();

        Assert.Equal("Put back 30 items.", editor.ResultMessage);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, store.Count);
        Assert.Equal(1, sender.Undos);

        await editor.UndoAsync();

        Assert.Equal(1, sender.Undos);
    }

    [Fact]
    public async Task ASaveThatChangedNothing_SaysSo_AndOffersNoUndo()
    {
        var (editor, _) = await LoadedAsync(5);
        editor.Fields[1].Text = "Rome";
        sender.WrittenWhenDone = 0;

        await editor.SaveAsync();

        Assert.Equal("No item needed a change.", editor.ResultMessage);
        Assert.False(editor.CanUndo);
    }

    private async Task<(BulkEditorViewModel Editor, BulkUndoStore Store)> LoadedAsync(int count)
    {
        var store = new BulkUndoStore();
        sender.Store = store;
        sender.WrittenWhenDone = count;
        var editor = new BulkEditorViewModel(sender, Localizer.Current, undoStore: store);
        await editor.LoadAsync([.. Enumerable.Range(0, count).Select(_ => Id<MediaItem>.New())]);
        return (editor, store);
    }

    /// <summary>
    /// Answers the aggregate, and saves like the service: it reports progress, keeps an undo and can be held until
    /// the save is stopped.
    /// </summary>
    internal sealed class SavingSender : BulkEditorSender
    {
        public List<EditMetadataCommand> Edits { get; } = [];

        public BulkUndoStore Store { get; set; } = new();

        public MetadataValidationError? Error { get; set; }

        public Exception? Failure { get; set; }

        public bool Block { get; set; }

        public int HandledBeforeBlocking { get; set; }

        public int WrittenBeforeBlocking { get; set; }

        public int WrittenWhenDone { get; set; }

        public int Undos { get; private set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case EditMetadataCommand edit:
                    Edits.Add(edit);
                    if (Failure is not null)
                    {
                        throw Failure;
                    }

                    if (Error is not null)
                    {
                        return (TResponse)(object)new EditMetadataResult([], Error);
                    }

                    if (Block)
                    {
                        for (var handled = 1; handled <= HandledBeforeBlocking; handled++)
                        {
                            edit.Progress?.Report(handled);
                        }

                        Store.Set(Snapshots(WrittenBeforeBlocking));
                        Started.SetResult();
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }

                    Store.Set(Snapshots(WrittenWhenDone));
                    return (TResponse)(object)new EditMetadataResult([.. Enumerable.Range(0, WrittenWhenDone).Select(_ => Id<MediaItem>.New())]);
                case UndoBulkEditCommand:
                    Undos++;
                    var restored = Store.Take();
                    return (TResponse)(object)new UndoBulkEditResult([.. restored.Keys]);
                default:
                    return await base.Send(request, cancellationToken);
            }
        }

        private static Dictionary<Id<MediaItem>, UserLayerSnapshot> Snapshots(int count) =>
            Enumerable.Range(0, count).ToDictionary(_ => Id<MediaItem>.New(), _ => new UserLayerSnapshot([], [], [], []));
    }
}
