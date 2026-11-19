using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels.Details;

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

    private MetadataEditorViewModel Create() => new(sender, TestLocalizer.EnUs());
}
