using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for accepting and dismissing new AI suggestions in the metadata editor (E07-S11-T03): both are staged until
/// Save, and Cancel drops them.
/// </summary>
public sealed class SuggestionActionTests
{
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task AcceptSuggestion_ShowsTheSuggestedValue_AndSaveSendsAcceptFieldSuggestion()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        Assert.True(title.CanDecideSuggestion);

        title.AcceptSuggestionCommand.Execute(null);

        Assert.Equal("Dog", title.Text);
        Assert.True(title.IsSuggestionAccepted);
        Assert.False(title.CanDecideSuggestion);
        Assert.True(editor.IsDirty);
        Assert.Empty(sender.Commands);

        await editor.SaveAsync();

        Assert.Equal([new MetadataEdit.AcceptFieldSuggestion(DescriptiveField.Title)], Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task DismissSuggestion_KeepsTheUsersValue_AndSaveSendsDismissFieldSuggestion()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];

        title.DismissSuggestionCommand.Execute(null);

        Assert.Equal("Beach day", title.Text);
        Assert.True(title.IsSuggestionDismissed);
        Assert.True(editor.IsDirty);
        await editor.SaveAsync();
        Assert.Equal([new MetadataEdit.DismissFieldSuggestion(DescriptiveField.Title)], Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task AcceptOrDismiss_ThenCancelOrUndo_IsDiscarded()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        title.AcceptSuggestionCommand.Execute(null);
        editor.Cancel();
        Assert.False(editor.IsDirty);
        Assert.Equal("Beach day", title.Text);
        Assert.True(title.CanDecideSuggestion);

        title.DismissSuggestionCommand.Execute(null);
        title.UndoCommand.Execute(null);

        Assert.False(editor.IsDirty);
        Assert.True(title.CanDecideSuggestion);
        Assert.Empty(sender.Commands);
    }

    [Fact]
    public async Task AcceptSuggestion_ThenTyping_SavesTheTypedValueInstead()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        title.AcceptSuggestionCommand.Execute(null);

        title.Text = "Rex the dog";

        Assert.False(title.IsSuggestionAccepted);
        await editor.SaveAsync();
        Assert.Equal([new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("Rex the dog"))], Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task Revert_AfterAccept_DropsTheStagedAccept()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        title.AcceptSuggestionCommand.Execute(null);

        title.RevertCommand.Execute(null);

        Assert.False(title.IsSuggestionAccepted);
        await editor.SaveAsync();
        Assert.Equal([new MetadataEdit.RevertField(DescriptiveField.Title)], Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task TagSuggestions_AcceptAndDismiss_AreStagedAndSentOnSave()
    {
        var editor = await ShowAsync();
        var sea = editor.Tags.Single(tag => tag.Text == "sea");
        var cat = editor.Tags.Single(tag => tag.Text == "cat");

        editor.AcceptTagSuggestion(sea);
        editor.DismissTagSuggestion(cat);

        Assert.True(sea.IsSuggestionAccepted);
        Assert.True(cat.IsSuggestionDismissed);
        Assert.True(editor.IsDirty);
        await editor.SaveAsync();
        Assert.Equal(
            [new MetadataEdit.AcceptTagSuggestion(TagName.From("sea")), new MetadataEdit.DismissTagSuggestion(TagName.From("cat"))],
            Assert.Single(sender.Commands).Edits.Where(edit => edit is MetadataEdit.AcceptTagSuggestion or MetadataEdit.DismissTagSuggestion));
    }

    [Fact]
    public async Task TagSuggestion_ARemovedTagAccepted_IsStruckThrough_AndCancelBringsItBack()
    {
        var editor = await ShowAsync();
        var cat = editor.Tags.Single(tag => tag.Text == "cat");

        editor.AcceptTagSuggestion(cat);

        Assert.True(cat.WillBeDropped);
        Assert.False(cat.CanRemove);

        editor.Cancel();

        Assert.False(editor.IsDirty);
        var back = editor.Tags.Single(tag => tag.Text == "cat");
        Assert.True(back.CanDecideSuggestion);
        Assert.False(back.WillBeDropped);
    }

    [Fact]
    public async Task TagSuggestion_UndoAfterAccept_Unstages()
    {
        var editor = await ShowAsync();
        var sea = editor.Tags.Single(tag => tag.Text == "sea");
        editor.AcceptTagSuggestion(sea);

        editor.UndoTagSuggestion(sea);

        Assert.False(editor.IsDirty);
        Assert.True(sea.CanDecideSuggestion);
    }

    [Fact]
    public async Task TagSuggestion_OnATagWithAStagedRemoval_IsNotDecidedAndNotSent()
    {
        var editor = await ShowAsync();
        var cat = editor.Tags.Single(tag => tag.Text == "cat");
        editor.RemoveTag(cat);

        editor.AcceptTagSuggestion(cat);

        Assert.False(cat.IsSuggestionAccepted);
        await editor.SaveAsync();
        Assert.DoesNotContain(Assert.Single(sender.Commands).Edits, edit => edit is MetadataEdit.AcceptTagSuggestion);
    }

    private async Task<MetadataEditorViewModel> ShowAsync()
    {
        var baseView = MetadataEditorTestData.Sample();
        var title = baseView[DescriptiveField.Title] with
        {
            Suggestion = new AiFieldValue(Id<AnalysisRun>.New(), MetadataEditorTestData.ItemId, DescriptiveField.Title, FieldValue.FromText("Dog"), Id<ModelRegistration>.New(), null, DateTimeOffset.UnixEpoch),
        };
        var fields = baseView.Fields.ToArray();
        fields[(int)DescriptiveField.Title] = title;
        sender.View = baseView with
        {
            Fields = fields,
            Tags =
            [
                new TagMetadataView(Tag("sea", MetadataSource.AI), TagSuggestionKind.Added),
                new TagMetadataView(Tag("cat", MetadataSource.User), TagSuggestionKind.Removed),
            ],
        };
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        return editor;
    }

    private static ResolvedTag Tag(string text, MetadataSource source) =>
        new(TagName.From(text), source, source == MetadataSource.User ? UserValueOrigin.Accepted : null, null, null, null);
}
