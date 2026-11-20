using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for the other-language form a text value carries (E06-S04-T09), such as the Russian title a describer wrote,
/// and for model-written tag forms that aren't forms.
/// </summary>
public sealed class TitleOtherFormTests
{
    private readonly ItemMetadataFixture fixture = new();

    private ItemMetadata Metadata => fixture.Metadata;

    [Theory]
    [InlineData(" Собака на пляже ", "Собака на пляже")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("dog on a BEACH", null)]
    public void WithOtherForm_KeepsARealForm_Only(string? form, string? expected) =>
        Assert.Equal(expected, FieldValue.FromText("Dog on a beach").WithOtherForm(form).OtherForm);

    [Fact]
    public void WithOtherForm_OnAList_Throws() =>
        Assert.Throws<InvalidOperationException>(() => FieldValue.FromList(["dog"]).WithOtherForm("собака"));

    [Fact]
    public void Values_CompareByText_NotByForm()
    {
        var plain = FieldValue.FromText("Dog on a beach");
        var withForm = plain.WithOtherForm("Собака на пляже");

        Assert.Equal(plain, withForm);
        Assert.Equal(plain.GetHashCode(), withForm.GetHashCode());
    }

    [Fact]
    public void AcceptedAiTitle_KeepsItsRussianForm_AndAnEditedOneHasNone()
    {
        AnalyzeTitle("Dog on a beach", "Собака на пляже");

        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());
        Assert.Equal("Собака на пляже", Metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.OtherForm);

        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("My dog"), fixture.Tick());
        Assert.Null(Metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.OtherForm);
    }

    [Fact]
    public void Reanalysis_SameTitleWithAnotherForm_RaisesNoSuggestion()
    {
        AnalyzeTitle("Dog on a beach", "Собака на пляже");
        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());

        AnalyzeTitle("Dog on a beach", "Пёс на пляже");

        Assert.False(Assert.Single(Metadata.UserValues).HasNewAiSuggestion);
    }

    [Theory]
    [InlineData("dog")]
    [InlineData(" DOG ")]
    public void AiTag_WithAWrittenFormEqualToTheTag_GetsTheDictionaryForm(string written)
    {
        var (run, _) = fixture.Analyze(null);

        var assignment = TagAssignment.Ai(run, TagName.From("dog"), null, fixture.Now, written);

        Assert.Equal("собака", assignment.OtherForm);
        Assert.True(assignment.IsOtherFormDerived);
    }

    private void AnalyzeTitle(string title, string form)
    {
        var model = ModelRegistration.Create("onnx", "describer", "1.0", null, null, fixture.Now);
        var run = AnalysisRun.Start(fixture.ItemId, ItemMetadataFixture.Caption, model, "v1", fixture.Tick());
        run.Complete(fixture.Now);
        var value = AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title).WithOtherForm(form), null, fixture.Now);
        Metadata.ApplyRun(run, [value], [], 1);
    }
}
