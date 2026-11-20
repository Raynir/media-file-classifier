using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the per-field Revert and Confirm actions of the metadata editor (E07-S11-T02): both are staged, take
/// effect on Save and are dropped by Cancel.
/// </summary>
public sealed class FieldActionTests
{
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task Confirm_StagesTheAiValue_AndSaveSendsConfirmField()
    {
        var editor = await ShowAsync();
        var description = editor.Descriptive[1];
        Assert.True(description.CanConfirm);

        description.ConfirmCommand.Execute(null);

        Assert.True(description.IsConfirmed);
        Assert.True(description.IsAccepted);
        Assert.False(description.IsAi);
        Assert.False(description.CanConfirm);
        Assert.True(editor.IsDirty);
        Assert.Empty(sender.Commands);

        await editor.SaveAsync();

        var command = Assert.Single(sender.Commands);
        Assert.Equal([new MetadataEdit.ConfirmField(DescriptiveField.Description)], command.Edits);
    }

    [Fact]
    public async Task Confirm_ThenCancel_DropsTheStagedAction()
    {
        var editor = await ShowAsync();
        editor.Descriptive[1].ConfirmCommand.Execute(null);

        editor.Cancel();

        Assert.False(editor.IsDirty);
        Assert.False(editor.Descriptive[1].IsConfirmed);
        Assert.True(editor.Descriptive[1].IsAi);
        Assert.True(editor.Descriptive[1].CanConfirm);
        Assert.Empty(sender.Commands);
    }

    [Fact]
    public async Task Confirm_ThenTyping_BecomesAnEditedValue()
    {
        var editor = await ShowAsync();
        var description = editor.Descriptive[1];
        description.ConfirmCommand.Execute(null);

        description.Text = "My own words";

        Assert.False(description.IsConfirmed);
        await editor.SaveAsync();
        Assert.Equal(
            [new MetadataEdit.SetField(DescriptiveField.Description, FieldValue.FromText("My own words"))],
            Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task Undo_TakesBackAStagedConfirmOrRevert()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        var description = editor.Descriptive[1];
        description.ConfirmCommand.Execute(null);
        title.RevertCommand.Execute(null);
        Assert.True(description.HasStagedAction);
        Assert.True(title.HasStagedAction);

        description.UndoCommand.Execute(null);
        title.UndoCommand.Execute(null);

        Assert.False(editor.IsDirty);
        Assert.False(description.HasStagedAction);
        Assert.Equal("Beach day", title.Text);
    }

    [Fact]
    public async Task Revert_IsStaged_SendsRevertField_AndCancelDropsIt()
    {
        var editor = await ShowAsync();
        var title = editor.Descriptive[0];
        title.RevertCommand.Execute(null);
        editor.Cancel();
        Assert.False(editor.IsDirty);

        title.RevertCommand.Execute(null);
        await editor.SaveAsync();

        Assert.Equal([new MetadataEdit.RevertField(DescriptiveField.Title)], Assert.Single(sender.Commands).Edits);
    }

    [Fact]
    public async Task Confirm_IsOfferedOnlyForAnUntouchedEffectiveAiValue()
    {
        var editor = await ShowAsync();

        Assert.False(editor.Descriptive[0].CanConfirm);
        Assert.False(editor.Transcript.CanConfirm);
        Assert.False(editor.Descriptive[2].CanConfirm);

        editor.Descriptive[1].Text = "Typed";
        Assert.False(editor.Descriptive[1].CanConfirm);
    }

    [Fact]
    public async Task NotAvailable_FieldTheModelCantProduce_AndNothingHolds_IsFlagged()
    {
        sender.Producible = DescriberFields.Title | DescriberFields.Description;

        var editor = await ShowAsync();

        Assert.True(editor.Descriptive[2].IsNotAvailable);
        Assert.False(editor.Descriptive[1].IsNotAvailable);
        Assert.False(editor.Analysis[0].IsNotAvailable);
        Assert.False(editor.Descriptive[3].IsNotAvailable);
        Assert.False(editor.Analysis[4].IsNotAvailable);
    }

    [Fact]
    public async Task NotAvailable_UnknownProducibleFields_FlagsNothing()
    {
        sender.Producible = null;

        var editor = await ShowAsync();

        Assert.DoesNotContain(editor.Descriptive.Concat(editor.Analysis), field => field.IsNotAvailable);
    }

    private async Task<MetadataEditorViewModel> ShowAsync()
    {
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        return editor;
    }
}
