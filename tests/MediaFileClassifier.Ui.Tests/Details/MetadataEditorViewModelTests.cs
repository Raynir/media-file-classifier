using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the metadata editor's view model (E07-S02): what it shows, and its staged edits.
/// </summary>
public sealed class MetadataEditorViewModelTests
{
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task Load_ShowsEveryField_TheTranscriptReadOnly_AndTheTagsWithTheirSource()
    {
        var editor = Create();

        await editor.LoadAsync(MetadataEditorTestData.ItemId);

        Assert.Equal(
            ["Beach day", "A sandy beach", string.Empty, string.Empty],
            editor.Descriptive.Select(field => field.Text));
        Assert.Equal(
            ["umbrella, towel", string.Empty, string.Empty, string.Empty, string.Empty],
            editor.Analysis.Select(field => field.Text));
        Assert.True(editor.HasTranscript);
        Assert.True(editor.Transcript.IsReadOnly);
        Assert.Equal("Hello there", editor.Transcript.Text);
        Assert.Equal(
            [("holiday", MetadataSource.User), ("sea", MetadataSource.AI), ("jpeg", MetadataSource.Technical)],
            editor.Tags.Select(tag => (tag.Text, tag.Source)));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Load_NoItemOrNoMetadata_ShowsEmptyFields()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);

        await editor.LoadAsync(null);

        Assert.All(editor.Descriptive.Concat(editor.Analysis), field => Assert.Equal(string.Empty, field.Text));
        Assert.False(editor.HasTranscript);
        Assert.Empty(editor.Tags);
        Assert.False(editor.HasTags);
    }

    [Fact]
    public async Task Cancel_DropsEditedText_AndHidesTheSaveBar()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);

        editor.Descriptive[1].Text = "Changed";
        Assert.True(editor.IsDirty);
        Assert.True(editor.Descriptive[1].IsDirty);

        editor.Cancel();

        Assert.Equal("A sandy beach", editor.Descriptive[1].Text);
        Assert.False(editor.Descriptive[1].IsDirty);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Save_SendsOneCommandWithEveryStagedFieldEdit_AndShowsTheStoredValuesAfterwards()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        editor.Descriptive[0].Text = "Beach holiday";
        editor.Analysis[0].Text = "umbrella; towel,  ball\nbucket";

        await editor.SaveAsync();

        var command = Assert.Single(sender.Commands);
        Assert.Equal([MetadataEditorTestData.ItemId], command.ItemIds);
        Assert.Equal(
            [
                new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("Beach holiday")),
                new MetadataEdit.SetField(DescriptiveField.Objects, FieldValue.FromList(["umbrella", "towel", "ball", "bucket"])),
            ],
            command.Edits);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_NothingStaged_SendsNothing()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);

        editor.Descriptive[1].Text = "Changed";
        editor.Descriptive[1].Text = "A sandy beach";
        await editor.SaveAsync();

        Assert.Empty(sender.Commands);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Revert_StagesTheAiValueAndSavesARevertEdit_AndTypingAgainSavesAValueInstead()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        var title = editor.Descriptive[0];
        Assert.True(title.CanRevert);
        Assert.False(editor.Descriptive[1].CanRevert);

        title.RevertCommand.Execute(null);
        Assert.True(title.IsDirty);
        await editor.SaveAsync();
        Assert.Equal([new MetadataEdit.RevertField(DescriptiveField.Title)], sender.Commands[0].Edits);

        title.RevertCommand.Execute(null);
        title.Text = "Typed";
        await editor.SaveAsync();
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("Typed"))], sender.Commands[1].Edits);
    }

    [Fact]
    public async Task Save_TheServiceRefusesAValue_ShowsTheMessageNextToTheFieldAndKeepsTheStagedChanges()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        sender.Error = new MetadataValidationError(MetadataValidationCode.TextTooLong, DescriptiveField.Title, Limit: 200);
        editor.Descriptive[0].Text = new string('x', 201);
        editor.Descriptive[1].Text = "Kept";

        await editor.SaveAsync();

        Assert.Equal("Too long: at most 200 characters.", editor.Descriptive[0].Error);
        Assert.Null(editor.Descriptive[1].Error);
        Assert.True(editor.IsDirty);
        Assert.Equal(new string('x', 201), editor.Descriptive[0].Text);
        Assert.Equal("Kept", editor.Descriptive[1].Text);

        editor.Descriptive[0].Text = "Fixed";
        Assert.Null(editor.Descriptive[0].Error);
    }

    [Fact]
    public async Task Save_TheServiceFails_ShowsAMessageInTheBarAndKeepsTheStagedChanges()
    {
        var editor = Create();
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        sender.Failure = new IOException("disk");
        editor.Descriptive[1].Text = "Changed";

        await editor.SaveAsync();

        Assert.Equal("Couldn't save the changes. Try again.", editor.SaveError);
        Assert.True(editor.IsDirty);
        Assert.False(editor.IsSaving);
    }

    private MetadataEditorViewModel Create() => new(sender, TestLocalizer.EnUs());
}
