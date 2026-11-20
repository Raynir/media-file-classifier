using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the tag editor inside the metadata editor (E07-S02-T02): staged adds, removals and rejections, and the
/// autocompletion.
/// </summary>
public sealed class TagEditorTests
{
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task AddTag_StagesAChipThatSaveSendsAsAddTag()
    {
        var editor = await LoadedAsync();

        Assert.True(editor.AddTag("  new   tag "));

        var chip = editor.Tags[^1];
        Assert.Equal(("new tag", MetadataSource.User, TagStage.Added), (chip.Text, chip.Source, chip.Stage));
        Assert.True(editor.IsDirty);

        await editor.SaveAsync();

        Assert.Equal([new MetadataEdit.AddTag("new tag")], sender.Commands.Single().Edits);
    }

    [Fact]
    public async Task AddTag_EmptyOrAlreadyShown_StagesNothing()
    {
        var editor = await LoadedAsync();

        Assert.False(editor.AddTag("   "));
        Assert.True(editor.AddTag("HOLIDAY"));
        Assert.True(editor.AddTag("sea"));

        Assert.Equal(3, editor.Tags.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task RemoveTag_OfAUserTag_IsStagedStruckAndSavedAsRemoveTag_AndCanBeUndone()
    {
        var editor = await LoadedAsync();
        var holiday = editor.Tags[0];

        editor.RemoveTag(holiday);

        Assert.True(holiday.IsRemoved);
        Assert.True(editor.IsDirty);
        Assert.Equal([new MetadataEdit.RemoveTag(TagName.From("holiday"))], editor.BuildEdits());

        editor.UndoTag(holiday);

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.BuildEdits());
    }

    [Fact]
    public async Task RemoveTag_OfAnAiOrFileTag_DoesNothing_ButRejectStagesIt()
    {
        var editor = await LoadedAsync();
        var sea = editor.Tags[1];
        var jpeg = editor.Tags[2];

        editor.RemoveTag(sea);
        editor.RemoveTag(jpeg);
        Assert.False(editor.IsDirty);

        editor.RejectTag(sea);
        editor.RejectTag(jpeg);

        Assert.Equal(
            [new MetadataEdit.RejectTag(TagName.From("sea")), new MetadataEdit.RejectTag(TagName.From("jpeg"))],
            editor.BuildEdits());
    }

    [Fact]
    public async Task RemovingOrRejectingATagThatWasJustAdded_DropsIt()
    {
        var editor = await LoadedAsync();
        editor.AddTag("temp");
        editor.AddTag("other");

        editor.RemoveTag(editor.Tags.Single(chip => chip.Text == "temp"));
        editor.RejectTag(editor.Tags.Single(chip => chip.Text == "other"));

        Assert.Equal(3, editor.Tags.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task AddTag_OfATagStagedForRemoval_UndoesTheRemoval()
    {
        var editor = await LoadedAsync();
        editor.RemoveTag(editor.Tags[0]);

        editor.AddTag("holiday");

        Assert.Equal(3, editor.Tags.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task BuildEdits_PutsFieldEditsFirst_ThenRemovalsAndRejections_ThenAdds()
    {
        var editor = await LoadedAsync();
        editor.AddTag("zzz");
        editor.RejectTag(editor.Tags[1]);
        editor.RemoveTag(editor.Tags[0]);
        editor.Descriptive[1].Text = "Changed";

        var edits = editor.BuildEdits();

        Assert.IsType<MetadataEdit.SetField>(edits[0]);
        Assert.Equal(
            [typeof(MetadataEdit.RemoveTag), typeof(MetadataEdit.RejectTag), typeof(MetadataEdit.AddTag)],
            edits.Skip(1).Select(edit => edit.GetType()));
    }

    [Fact]
    public async Task Cancel_DropsEveryStagedTagChange()
    {
        var editor = await LoadedAsync();
        editor.AddTag("zzz");
        editor.RemoveTag(editor.Tags[0]);
        editor.NewTagText = "typing";

        editor.Cancel();

        Assert.Equal(["holiday", "sea", "jpeg"], editor.Tags.Select(chip => chip.Text));
        Assert.All(editor.Tags, chip => Assert.Equal(TagStage.Unchanged, chip.Stage));
        Assert.False(editor.IsDirty);
        Assert.Equal(string.Empty, editor.NewTagText);
    }

    [Fact]
    public async Task Save_TheServiceRefusesATag_ShowsTheErrorUnderTheTagInputAndKeepsTheChips()
    {
        var editor = await LoadedAsync();
        editor.AddTag("bad");
        sender.Error = new MetadataValidationError(MetadataValidationCode.TagInvalidCharacter, Tag: "bad");

        await editor.SaveAsync();

        Assert.NotNull(editor.TagError);
        Assert.Contains("bad", editor.TagError);
        Assert.Null(editor.SaveError);
        Assert.True(editor.IsDirty);
        Assert.Equal(4, editor.Tags.Count);
    }

    [Fact]
    public async Task Save_ReloadsTheTagsFromTheLibrary()
    {
        var editor = await LoadedAsync();
        editor.AddTag("zzz");

        await editor.SaveAsync();

        Assert.Equal(3, editor.Tags.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Suggestions_FollowTheTypedText_MostUsedFirst_WithoutTagsTheItemShows()
    {
        sender.KnownTags.AddRange([new("holiday", 9), new("hobby", 5), new("hotel", 3)]);
        var editor = await LoadedAsync();

        editor.NewTagText = "ho";
        await editor.SuggestionsLoading;

        Assert.Equal(["hobby", "hotel"], editor.Suggestions);
        Assert.Equal("ho", sender.Prefixes[^1]);
    }

    [Fact]
    public async Task Suggestions_ALookupThatFails_LeavesNone()
    {
        var editor = await LoadedAsync();
        sender.SuggestFailure = new IOException("db");

        editor.NewTagText = "x";
        await editor.SuggestionsLoading;

        Assert.Empty(editor.Suggestions);
    }

    private async Task<MetadataEditorViewModel> LoadedAsync()
    {
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        return editor;
    }
}
