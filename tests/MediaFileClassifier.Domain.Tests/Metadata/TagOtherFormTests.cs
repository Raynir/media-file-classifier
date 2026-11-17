using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for the other-language forms of tags (D035, D047, D348, E06-S04-T09): which assignments derive one from the tag
/// dictionary, and that accepting a tag keeps it.
/// </summary>
public sealed class TagOtherFormTests
{
    private readonly ItemMetadataFixture fixture = new();

    private ItemMetadata Metadata => fixture.Metadata;

    [Fact]
    public void AiTag_GetsADerivedRussianForm()
    {
        fixture.Analyze(null, ["dog", "flux capacitor"]);

        var tags = Metadata.GetEffectiveTags().ToDictionary(tag => tag.Tag.Value);
        Assert.Equal("собака", tags["dog"].OtherForm);
        Assert.True(tags["dog"].IsOtherFormDerived);
        Assert.Null(tags["flux capacitor"].OtherForm);
        Assert.False(tags["flux capacitor"].IsOtherFormDerived);
    }

    [Fact]
    public void AiTag_WithAFormTheModelWrote_KeepsItUnmarked()
    {
        var (run, _) = fixture.Analyze(null);

        var assignment = TagAssignment.Ai(run, TagName.From("dog"), null, fixture.Now, "пёс");

        Assert.Equal("пёс", assignment.OtherForm);
        Assert.False(assignment.IsOtherFormDerived);
    }

    [Theory]
    [InlineData("Кошки", "cat")]
    [InlineData("Dogs", "собака")]
    public void UserTag_IsStoredAsTyped_WithADerivedForm(string typed, string expected)
    {
        Metadata.AddTag(TagName.From(typed), fixture.Tick());

        var tag = Assert.Single(Metadata.GetEffectiveTags());
        Assert.Equal(typed, tag.Tag.Value);
        Assert.Equal(expected, tag.OtherForm);
        Assert.True(tag.IsOtherFormDerived);
    }

    [Fact]
    public void FileTag_GetsADerivedForm()
    {
        Metadata.SetTechnicalTags([TagName.From("beach")], fixture.Tick());

        var tag = Assert.Single(Metadata.GetEffectiveTags());
        Assert.Equal("пляж", tag.OtherForm);
        Assert.True(tag.IsOtherFormDerived);
    }

    [Fact]
    public void AcceptedTag_CopiesTheAiTagsForm()
    {
        var model = ModelRegistration.Create("onnx", "describer", "1.0", null, null, fixture.Now);
        var run = AnalysisRun.Start(fixture.ItemId, ItemMetadataFixture.Caption, model, "v1", fixture.Tick());
        run.Complete(fixture.Now);
        Metadata.ApplyRun(run, [], [TagAssignment.Ai(run, TagName.From("dog"), null, fixture.Now, "пёс")], 1);

        Metadata.AcceptTag(TagName.From("dog"), fixture.Tick());

        var accepted = Assert.Single(Metadata.Tags, tag => tag.Source == MetadataSource.User);
        Assert.Equal("пёс", accepted.OtherForm);
        Assert.False(accepted.IsOtherFormDerived);
    }

    [Fact]
    public void AcceptedDerivedTag_StaysMarkedAsDerived()
    {
        fixture.Analyze(null, ["cat"]);

        Metadata.AcceptAll(fixture.Tick());

        var tag = Assert.Single(Metadata.GetEffectiveTags());
        Assert.Equal(MetadataSource.User, tag.Source);
        Assert.Equal("кошка", tag.OtherForm);
        Assert.True(tag.IsOtherFormDerived);
    }

    [Fact]
    public void RejectedTag_TakesItsFormAlong()
    {
        fixture.Analyze(null, ["cat"]);

        Metadata.RejectTag(TagName.From("cat"), fixture.Tick());

        Assert.Empty(Metadata.GetEffectiveTags());
    }

    [Fact]
    public void Constructor_DerivedWithoutAForm_Throws() =>
        Assert.Throws<ArgumentException>(() => new TagAssignment(
            fixture.ItemId, TagName.From("cat"), MetadataSource.Technical, null, null, null, null, null, fixture.Now, null, true));
}
