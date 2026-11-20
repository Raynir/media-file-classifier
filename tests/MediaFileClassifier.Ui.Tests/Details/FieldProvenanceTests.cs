using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the provenance marks of the metadata editor's fields (E07-S11-T01): which mark a value gets, the model
/// and date in its tooltip, and the new-suggestion mark.
/// </summary>
public sealed class FieldProvenanceTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly Id<ModelRegistration> model = Id<ModelRegistration>.New();
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public async Task Load_AiValue_GetsTheAiMark_WithTheModelAndDateInTheTooltip()
    {
        var editor = await ShowAsync(Field(DescriptiveField.Description, "A beach", MetadataSource.AI));

        var field = editor.Descriptive[1];
        Assert.Equal(FieldProvenance.Ai, field.Provenance);
        Assert.True(field.IsAi);
        Assert.Contains("describe-2", field.ProvenanceTip);
        Assert.Contains("2026", field.ProvenanceTip);
    }

    [Fact]
    public async Task Load_AiValueWithAnUnknownModel_NamesNoModel()
    {
        var editor = await ShowAsync(Field(DescriptiveField.Description, "A beach", MetadataSource.AI), names: new Dictionary<Id<ModelRegistration>, string>());

        Assert.Equal(FieldProvenance.Ai, editor.Descriptive[1].Provenance);
        Assert.StartsWith("Written by the AI on", editor.Descriptive[1].ProvenanceTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_UserEditedAndAcceptedValues_GetDistinctMarks()
    {
        var edited = Field(DescriptiveField.Title, "Rex", MetadataSource.User, UserValueOrigin.Edited);
        var accepted = Field(DescriptiveField.Description, "A beach", MetadataSource.User, UserValueOrigin.Accepted);

        var editor = await ShowAsync(edited, accepted);

        Assert.Equal(FieldProvenance.Edited, editor.Descriptive[0].Provenance);
        Assert.True(editor.Descriptive[0].IsEditedValue);
        Assert.Contains("Edited by you", editor.Descriptive[0].ProvenanceTip);
        Assert.Equal(FieldProvenance.Accepted, editor.Descriptive[1].Provenance);
        Assert.True(editor.Descriptive[1].IsAccepted);
        Assert.Contains("describe-2", editor.Descriptive[1].ProvenanceTip);
    }

    [Fact]
    public async Task Load_FileValueAndEmptyField_HaveNoMark()
    {
        var editor = await ShowAsync(Field(DescriptiveField.Notes, "from file", MetadataSource.Technical));

        Assert.Equal(FieldProvenance.None, editor.Descriptive[3].Provenance);
        Assert.Equal(string.Empty, editor.Descriptive[3].ProvenanceTip);
        Assert.Equal(FieldProvenance.None, editor.Descriptive[2].Provenance);
    }

    [Fact]
    public async Task Load_UserValueWithANewSuggestion_ShowsTheSuggestionBesideTheMark()
    {
        var suggested = new AiFieldValue(Id<AnalysisRun>.New(), MetadataEditorTestData.ItemId, DescriptiveField.Title, FieldValue.FromText("Dog"), model, null, At);
        var field = Field(DescriptiveField.Title, "Rex", MetadataSource.User, UserValueOrigin.Edited) with { Suggestion = suggested };

        var editor = await ShowAsync(field);

        Assert.True(editor.Descriptive[0].HasSuggestion);
        Assert.Equal("Dog", editor.Descriptive[0].SuggestionText);
        Assert.True(editor.Descriptive[0].IsEditedValue);
        Assert.Contains("describe-2", editor.Descriptive[0].SuggestionTip);
        Assert.False(editor.Descriptive[1].HasSuggestion);
    }

    private async Task<MetadataEditorViewModel> ShowAsync(FieldMetadataView field, FieldMetadataView? second = null, IReadOnlyDictionary<Id<ModelRegistration>, string>? names = null)
    {
        var fields = DescriptiveFields.All.Select(f => new FieldMetadataView(f, null, null, null, null, null)).ToArray();
        foreach (var changed in new[] { field, second }.OfType<FieldMetadataView>())
        {
            fields[(int)changed.Field] = changed;
        }

        sender.View = new ItemMetadataView(MetadataEditorTestData.ItemId, fields, [], [], false)
        {
            ModelNames = names ?? new Dictionary<Id<ModelRegistration>, string> { [model] = "describe-2" },
        };
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        await editor.LoadAsync(MetadataEditorTestData.ItemId);
        return editor;
    }

    private FieldMetadataView Field(DescriptiveField field, string text, MetadataSource source, UserValueOrigin? origin = null)
    {
        var value = FieldValue.FromText(text);
        var run = Id<AnalysisRun>.New();
        var resolved = new ResolvedField(field, value, source, origin, run, source == MetadataSource.Technical ? null : model, null, At, null);
        UserFieldValue? user = origin switch
        {
            UserValueOrigin.Edited => UserFieldValue.Edited(MetadataEditorTestData.ItemId, field, value, At),
            UserValueOrigin.Accepted => UserFieldValue.Accepted(MetadataEditorTestData.ItemId, field, value, new RunReference(run, "describe", model, "p1", At), At),
            _ => null,
        };
        return new FieldMetadataView(field, resolved, user, null, null, null);
    }
}
