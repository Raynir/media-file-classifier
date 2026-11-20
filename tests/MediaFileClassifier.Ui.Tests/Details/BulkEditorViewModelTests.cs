using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the metadata editor in bulk mode (E07-S03-T01): the aggregated display and the staged changes.
/// </summary>
public sealed class BulkEditorViewModelTests
{
    private const int Total = 120;

    private readonly BulkEditorSender sender = new();

    [Fact]
    public async Task Load_ShowsSharedValues_MultipleValues_AndTagCounts()
    {
        var editor = await LoadedAsync();

        var title = editor.Fields[0];
        Assert.Equal("Trip", title.Text);
        Assert.False(title.IsMixed);
        Assert.Equal(string.Empty, title.Placeholder);

        var description = editor.Fields[1];
        Assert.Equal(string.Empty, description.Text);
        Assert.True(description.IsMixed);
        Assert.Equal("Multiple values", description.Placeholder);
        Assert.Equal("37 of 120 items have a value", description.Counts);

        Assert.Equal(["dog (37 of 120)", "holiday (120 of 120)"], editor.Tags.Select(tag => tag.Label));
        Assert.Equal(Total, editor.ItemCount);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task UntouchedFields_AreLeftAlone_AndTypingSetsOnlyThatField()
    {
        var editor = await LoadedAsync();

        Assert.Empty(editor.BuildEdits());

        editor.Fields[1].Text = "Rome";

        Assert.True(editor.IsDirty);
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Description, FieldValue.FromText("Rome"))], editor.BuildEdits());
    }

    [Fact]
    public async Task TypingAndThenRestoringTheLoadedText_StagesNothing()
    {
        var editor = await LoadedAsync();

        editor.Fields[0].Text = "Other";
        editor.Fields[0].Text = "Trip";
        editor.Fields[1].Text = "x";
        editor.Fields[1].Text = string.Empty;

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.BuildEdits());
    }

    [Fact]
    public async Task Clear_OnAMixedField_StagesAnEmptyValueOnEveryItem_AndTypingTakesItBack()
    {
        var editor = await LoadedAsync();
        var description = editor.Fields[1];

        description.ClearCommand.Execute(null);

        Assert.True(description.IsCleared);
        Assert.True(editor.IsDirty);
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Description, FieldValue.FromText(string.Empty))], editor.BuildEdits());

        description.Text = "Rome";

        Assert.False(description.IsCleared);
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Description, FieldValue.FromText("Rome"))], editor.BuildEdits());
    }

    [Fact]
    public async Task Revert_StagesRevertField_OnlyWhereItemsHaveAUserValue_AndUndoDropsIt()
    {
        var editor = await LoadedAsync();
        var title = editor.Fields[0];
        var notes = editor.Fields[3];
        Assert.True(title.CanRevert);
        Assert.False(notes.CanRevert);

        title.RevertCommand.Execute(null);

        Assert.True(title.IsReverted);
        Assert.Equal([new MetadataEdit.RevertField(DescriptiveField.Title)], editor.BuildEdits());

        title.UndoCommand.Execute(null);

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.BuildEdits());
    }

    [Fact]
    public async Task Tags_AddRemoveAndReject_AreStaged_InTheOrderTheServiceNeeds()
    {
        var editor = await LoadedAsync();
        var dog = editor.Tags[0];
        var holiday = editor.Tags[1];

        Assert.True(editor.AddTag("  Trip to   Italy "));
        editor.RemoveTag(holiday);
        editor.RejectTag(dog);

        Assert.Equal(
            [
                new MetadataEdit.RemoveTag(TagName.From("holiday")),
                new MetadataEdit.RejectTag(TagName.From("dog")),
                new MetadataEdit.AddTag("Trip to Italy"),
            ],
            editor.BuildEdits());
        Assert.Contains(editor.Tags, tag => tag.Label == "Trip to Italy (adds to all 120)");
    }

    [Fact]
    public async Task RemoveTag_NotOffered_ForATagNoItemHasAsAUserTag_AndUndoTakesBackAStagedRemoval()
    {
        var editor = await LoadedAsync();
        var dog = editor.Tags[0];
        var holiday = editor.Tags[1];
        Assert.False(dog.CanRemove);
        Assert.True(dog.CanReject);

        editor.RemoveTag(dog);
        editor.RemoveTag(holiday);
        editor.UndoTag(holiday);

        Assert.Equal(TagStage.Unchanged, dog.Stage);
        Assert.Equal(TagStage.Unchanged, holiday.Stage);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task AddTag_OfATagSomeItemsHave_IsStagedForTheRest_AndATagAllHaveIsAlreadyDone()
    {
        var editor = await LoadedAsync();
        sender.View = sender.View with { Tags = [.. sender.View.Tags, new BulkTagView(TagName.From("sea"), 10, 10, 0, 0)] };
        await editor.LoadAsync(Ids());

        Assert.True(editor.AddTag("SEA"));
        Assert.True(editor.AddTag("holiday"));

        Assert.Equal(TagStage.Added, editor.Tags.Single(tag => tag.Text == "sea").Stage);
        Assert.Equal(TagStage.Unchanged, editor.Tags.Single(tag => tag.Text == "holiday").Stage);
        Assert.Equal([new MetadataEdit.AddTag("sea")], editor.BuildEdits());
    }

    [Fact]
    public async Task AddTag_Empty_StagesNothing_AndATypedTagCanBeRemovedAgain()
    {
        var editor = await LoadedAsync();

        Assert.False(editor.AddTag("   "));
        editor.NewTagText = "fresh";
        editor.AddTypedTagCommand.Execute(null);
        var fresh = editor.Tags.Single(tag => tag.Text == "fresh");
        Assert.Equal(string.Empty, editor.NewTagText);
        Assert.True(fresh.CanRemove);

        editor.RemoveTag(fresh);

        Assert.DoesNotContain(editor.Tags, tag => tag.Text == "fresh");
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Cancel_DropsEveryStagedChange()
    {
        var editor = await LoadedAsync();
        editor.Fields[0].Text = "Other";
        editor.Fields[1].ClearCommand.Execute(null);
        editor.AddTag("fresh");
        editor.RemoveTag(editor.Tags[1]);

        editor.Cancel();

        Assert.False(editor.IsDirty);
        Assert.Equal("Trip", editor.Fields[0].Text);
        Assert.Empty(editor.BuildEdits());
        Assert.Equal(2, editor.Tags.Count);
    }

    [Fact]
    public async Task Load_AgainDropsStagedChanges_AndAnEmptySelectionEmptiesTheEditor()
    {
        var editor = await LoadedAsync();
        editor.Fields[0].Text = "Other";

        await editor.LoadAsync([]);

        Assert.False(editor.IsDirty);
        Assert.Equal(0, editor.ItemCount);
        Assert.Empty(editor.Tags);
        Assert.Equal(string.Empty, editor.Fields[0].Text);
    }

    private static List<Id<MediaItem>> Ids() => [.. Enumerable.Range(0, Total).Select(_ => Id<MediaItem>.New())];

    private async Task<BulkEditorViewModel> LoadedAsync()
    {
        var editor = new BulkEditorViewModel(sender, Localizer.Current);
        await editor.LoadAsync(Ids());
        return editor;
    }
}

/// <summary>
/// Serves the aggregate of a selection of 120 items: a shared title, a description on 37 of them, and two tags.
/// </summary>
internal sealed class BulkEditorSender : FakeSender
{
    public BulkMetadataView View { get; set; } = new(
        120,
        [.. DescriptiveFields.All.Select(field => field switch
        {
            DescriptiveField.Title => new BulkFieldView(field, FieldValue.FromText("Trip"), false, 120, 120, 0, 0),
            DescriptiveField.Description => new BulkFieldView(field, null, true, 37, 0, 37, 0),
            _ => new BulkFieldView(field, null, false, 0, 0, 0, 0),
        })],
        [
            new BulkTagView(TagName.From("dog"), 37, 0, 37, 0),
            new BulkTagView(TagName.From("holiday"), 120, 120, 0, 0),
        ]);

    public List<GetBulkMetadataQuery> Queries { get; } = [];

    protected override object? Handle(object request)
    {
        if (request is GetBulkMetadataQuery query)
        {
            Queries.Add(query);
            return View with { ItemCount = query.ItemIds.Count };
        }

        return base.Handle(request);
    }
}
