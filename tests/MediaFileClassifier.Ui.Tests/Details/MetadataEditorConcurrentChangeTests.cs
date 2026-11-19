using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the editor's handling of background changes (E07-S12-T02): staged edits are never discarded.
/// </summary>
public sealed class MetadataEditorConcurrentChangeTests
{
    private static readonly IReadOnlySet<DescriptiveField> None = new HashSet<DescriptiveField>();

    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task NothingStaged_ShowsTheNewStoredValues()
    {
        var editor = await LoadedAsync();
        sender.View = MetadataEditorTestData.Sample(description: "A re-analysed beach");

        await editor.MergeChangesAsync(Fields(DescriptiveField.Description), tagsChanged: false);

        Assert.Equal("A re-analysed beach", editor.Descriptive[1].Text);
        Assert.False(editor.IsDirty);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task ChangedFieldsDontOverlapTheStagedOnes_RefreshesTheOthers_KeepsTheStagedText_AndShowsNoNotice()
    {
        var editor = await LoadedAsync();
        editor.Descriptive[0].Text = "Beach holiday";
        sender.View = MetadataEditorTestData.Sample(description: "A re-analysed beach");

        await editor.MergeChangesAsync(Fields(DescriptiveField.Description), tagsChanged: false);

        Assert.Equal("A re-analysed beach", editor.Descriptive[1].Text);
        Assert.False(editor.Descriptive[1].IsDirty);
        Assert.Equal("Beach holiday", editor.Descriptive[0].Text);
        Assert.True(editor.Descriptive[0].IsDirty);
        Assert.True(editor.IsDirty);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task ChangedFieldOverlapsAStagedOne_KeepsTheStagedText_NamesTheFieldInANotice_AndSavesTheStagedValue()
    {
        var editor = await LoadedAsync();
        editor.Descriptive[1].Text = "My description";
        sender.View = MetadataEditorTestData.Sample(description: "A re-analysed beach");

        await editor.MergeChangesAsync(Fields(DescriptiveField.Description), tagsChanged: false);

        Assert.Equal("My description", editor.Descriptive[1].Text);
        Assert.True(editor.Descriptive[1].IsDirty);
        Assert.Equal("Description changed elsewhere. Your edit is kept; saving replaces the new value.", editor.Notice);

        await editor.SaveAsync();

        Assert.Single(sender.Commands);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task ChangedFieldNowEqualsTheStagedText_IsNoLongerAChange()
    {
        var editor = await LoadedAsync();
        editor.Descriptive[1].Text = "A re-analysed beach";
        sender.View = MetadataEditorTestData.Sample(description: "A re-analysed beach");

        await editor.MergeChangesAsync(Fields(DescriptiveField.Description), tagsChanged: false);

        Assert.False(editor.IsDirty);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task StagedTags_SurviveTheMerge_WhileOtherTagsRefresh()
    {
        var editor = await LoadedAsync();
        editor.AddTag("new one");
        editor.RemoveTag(editor.Tags.First(tag => tag.Text == "holiday"));
        sender.View = MetadataEditorTestData.Sample(aiTag: "waves");

        await editor.MergeChangesAsync(None, tagsChanged: true);

        Assert.Equal(
            [("holiday", TagStage.Removed), ("waves", TagStage.Unchanged), ("jpeg", TagStage.Unchanged), ("new one", TagStage.Added)],
            editor.Tags.Select(tag => (tag.Text, tag.Stage)));
        Assert.True(editor.IsDirty);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task StagedTagChangeForATagThatIsGone_IsDropped_AndNothingIsLeftToSave()
    {
        var editor = await LoadedAsync();
        editor.RejectTag(editor.Tags.First(tag => tag.Text == "sea"));
        sender.View = MetadataEditorTestData.Sample(aiTag: "waves");

        await editor.MergeChangesAsync(None, tagsChanged: true);

        Assert.DoesNotContain(editor.Tags, tag => tag.Text == "sea");
        Assert.All(editor.Tags, tag => Assert.Equal(TagStage.Unchanged, tag.Stage));
        Assert.False(editor.IsDirty);
        Assert.Null(editor.Notice);
    }

    [Fact]
    public async Task StagedTagAddedThatIsStoredNow_ShowsANoticeNamingTheTags()
    {
        var editor = await LoadedAsync();
        editor.Descriptive[0].Text = "Kept";
        editor.AddTag("waves");
        sender.View = MetadataEditorTestData.Sample(aiTag: "waves");

        await editor.MergeChangesAsync(None, tagsChanged: true);

        Assert.Equal("Tags changed elsewhere. Your edit is kept; saving replaces the new value.", editor.Notice);
        Assert.Equal("Kept", editor.Descriptive[0].Text);
        Assert.Single(editor.Tags, tag => tag.Text == "waves");
    }

    [Fact]
    public async Task WhileASaveRuns_TheMergeLeavesTheEditorAlone()
    {
        var editor = await LoadedAsync();
        editor.Descriptive[1].Text = "My description";
        var save = editor.SaveAsync();

        await editor.MergeChangesAsync(Fields(DescriptiveField.Description), tagsChanged: false);
        await save;

        Assert.Null(editor.Notice);
        Assert.Single(sender.Commands);
    }

    private static HashSet<DescriptiveField> Fields(params DescriptiveField[] fields) => [.. fields];

    private async Task<MetadataEditorViewModel> LoadedAsync()
    {
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        return editor;
    }
}
