using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for <see cref="ItemMetadata"/>: the effective-value rule, user origins, tag tombstones and re-analysis (D020, D021).
/// </summary>
public sealed class ItemMetadataTests
{
    private readonly ItemMetadataFixture fixture = new();

    private ItemMetadata Metadata => fixture.Metadata;

    [Fact]
    public void EffectiveValue_PrefersUserThenAiThenTechnical()
    {
        Metadata.SetTechnicalValues([new TechnicalFieldValue(fixture.ItemId, DescriptiveField.Title, FieldValue.FromText("IMG_0001"), fixture.Now)]);
        Assert.Equal(MetadataSource.Technical, Metadata.GetEffectiveValue(DescriptiveField.Title)?.Source);

        var (run, _) = fixture.Analyze("Dog on a beach");
        var ai = Metadata.GetEffectiveValue(DescriptiveField.Title);
        Assert.Equal(MetadataSource.AI, ai?.Source);
        Assert.Equal(run.Id, ai?.RunId);
        Assert.Equal(run.ModelId, ai?.ModelId);
        Assert.Equal(0.9, ai?.Confidence);

        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex at the sea"), fixture.Tick());
        var user = Metadata.GetEffectiveValue(DescriptiveField.Title);
        Assert.Equal(MetadataSource.User, user?.Source);
        Assert.Equal(UserValueOrigin.Edited, user?.Origin);
        Assert.Equal("Rex at the sea", user?.Value.Text);
    }

    [Fact]
    public void EffectiveValue_IsNullWhenNoLayerHasOne()
    {
        Assert.Null(Metadata.GetEffectiveValue(DescriptiveField.Summary));
    }

    [Fact]
    public void EmptyUserValue_HidesAiValue()
    {
        fixture.Analyze("Dog on a beach");

        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText(string.Empty), fixture.Tick());

        Assert.Equal(string.Empty, fixture.EffectiveTitle());
    }

    [Fact]
    public void Reanalysis_FieldWithoutUserValue_TakesNewAiValue()
    {
        fixture.Analyze("Dog on a beach");

        var (_, result) = fixture.Analyze("Dog running on a beach");

        Assert.Equal("Dog running on a beach", fixture.EffectiveTitle());
        Assert.True(result.EffectiveValuesChanged);
    }

    [Fact]
    public void Reanalysis_NeverOverwritesEditedValue_AndFlagsDifferentSuggestion()
    {
        fixture.Analyze("Dog on a beach");
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex at the sea"), fixture.Tick());

        var (run, result) = fixture.Analyze("Dog running on a beach");

        var title = Metadata.GetEffectiveValue(DescriptiveField.Title);
        Assert.Equal("Rex at the sea", title?.Value.Text);
        Assert.Equal(UserValueOrigin.Edited, title?.Origin);
        Assert.Equal(run.Id, title?.SuggestionRunId);
        Assert.False(result.EffectiveValuesChanged);
    }

    [Fact]
    public void Reanalysis_NeverOverwritesAcceptedValue_AndKeepsItsRunReference()
    {
        var (first, _) = fixture.Analyze("Dog on a beach");
        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());

        var (second, _) = fixture.Analyze("Dog running on a beach");

        var user = Assert.Single(Metadata.UserValues);
        Assert.Equal(UserValueOrigin.Accepted, user.Origin);
        Assert.Equal("Dog on a beach", user.Value.Text);
        Assert.Equal(first.Id, user.AcceptedFrom?.RunId);
        Assert.Equal("v1", user.AcceptedFrom?.PromptVersion);
        Assert.Equal(second.Id, user.SuggestionRunId);
    }

    [Fact]
    public void Reanalysis_SameValueAsUser_RaisesNoSuggestion()
    {
        fixture.Analyze("Dog on a beach");
        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());

        fixture.Analyze("Dog on a beach");

        Assert.False(Assert.Single(Metadata.UserValues).HasNewAiSuggestion);
    }

    [Fact]
    public void Reanalysis_WithSameResults_DoesNotChangeEffectiveValues()
    {
        fixture.Analyze("Dog on a beach", ["dog", "beach"]);

        var (_, result) = fixture.Analyze("Dog on a beach", ["beach", "dog"]);

        Assert.False(result.EffectiveValuesChanged);
    }

    [Fact]
    public void AcceptSuggestion_ReplacesUserValueWithAcceptedNewAiValue()
    {
        fixture.Analyze("Dog on a beach");
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), fixture.Tick());
        var (run, _) = fixture.Analyze("Dog running on a beach");

        Metadata.AcceptSuggestion(DescriptiveField.Title, fixture.Tick());

        var user = Assert.Single(Metadata.UserValues);
        Assert.Equal("Dog running on a beach", user.Value.Text);
        Assert.Equal(UserValueOrigin.Accepted, user.Origin);
        Assert.Equal(run.Id, user.AcceptedFrom?.RunId);
        Assert.False(user.HasNewAiSuggestion);
    }

    [Fact]
    public void DismissSuggestion_KeepsUserValue()
    {
        fixture.Analyze("Dog on a beach");
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), fixture.Tick());
        fixture.Analyze("Dog running on a beach");

        Metadata.DismissSuggestion(DescriptiveField.Title);

        Assert.Equal("Rex", fixture.EffectiveTitle());
        Assert.False(Assert.Single(Metadata.UserValues).HasNewAiSuggestion);
    }

    [Fact]
    public void ClearUserValue_RevertsToActiveAiValue()
    {
        fixture.Analyze("Dog on a beach");
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), fixture.Tick());

        Metadata.ClearUserValue(DescriptiveField.Title);

        Assert.Equal("Dog on a beach", fixture.EffectiveTitle());
    }

    [Fact]
    public void RejectedTag_IsNotAddedAgainByReanalysis()
    {
        fixture.Analyze(null, ["dog", "cat"]);

        Metadata.RejectTag(TagName.From("cat"), fixture.Tick());
        fixture.Analyze(null, ["dog", "Cat"]);

        Assert.Equal(["dog"], fixture.EffectiveTags());
        var rejection = Assert.Single(Metadata.Rejections);
        Assert.Equal("cat", rejection.Tag.Value);
        Assert.NotNull(rejection.RejectedFrom);
    }

    [Fact]
    public void RejectedTag_HidesTechnicalTag()
    {
        Metadata.SetTechnicalTags([TagName.From("vacation")], fixture.Now);

        Metadata.RejectTag(TagName.From("vacation"), fixture.Tick());

        Assert.Empty(fixture.EffectiveTags());
    }

    [Fact]
    public void AddingRejectedTag_RemovesTombstone()
    {
        fixture.Analyze(null, ["cat"]);
        Metadata.RejectTag(TagName.From("cat"), fixture.Tick());

        Metadata.AddTag(TagName.From("Cat"), fixture.Tick());

        Assert.Empty(Metadata.Rejections);
        var tag = Assert.Single(Metadata.GetEffectiveTags());
        Assert.Equal(MetadataSource.User, tag.Source);
        Assert.Equal(UserValueOrigin.Edited, tag.Origin);
    }

    [Fact]
    public void AcceptedTag_StaysWhenNewRunDropsIt()
    {
        var (first, _) = fixture.Analyze(null, ["dog", "beach"]);
        Metadata.AcceptTag(TagName.From("beach"), fixture.Tick());

        fixture.Analyze(null, ["dog"]);

        Assert.Equal(["beach", "dog"], fixture.EffectiveTags());
        var beach = Metadata.GetEffectiveTags().Single(tag => tag.Tag == TagName.From("beach"));
        Assert.Equal(UserValueOrigin.Accepted, beach.Origin);
        Assert.Equal(first.Id, beach.RunId);
    }

    [Fact]
    public void Tags_AreDeduplicatedAcrossSources()
    {
        Metadata.SetTechnicalTags([TagName.From("Beach")], fixture.Now);
        fixture.Analyze(null, ["beach"]);
        Metadata.AddTag(TagName.From("BEACH"), fixture.Tick());

        var tag = Assert.Single(Metadata.GetEffectiveTags());
        Assert.Equal(MetadataSource.User, tag.Source);
    }

    [Fact]
    public void RejectActiveRun_HidesItsAiLayerButKeepsUserValues()
    {
        fixture.Analyze("Dog on a beach", ["dog"]);
        Metadata.EditValue(DescriptiveField.Summary, FieldValue.FromText("My summary"), fixture.Tick());

        Metadata.RejectActiveRun(ItemMetadataFixture.Caption, fixture.Tick());

        Assert.Null(fixture.EffectiveTitle());
        Assert.Empty(fixture.EffectiveTags());
        Assert.Equal("My summary", Metadata.GetEffectiveValue(DescriptiveField.Summary)?.Value.Text);
    }

    [Fact]
    public void AcceptAll_MakesEveryAiValueAndTagUserOwned()
    {
        fixture.Analyze("Dog on a beach", ["dog", "beach"]);

        Metadata.AcceptAll(fixture.Tick());
        fixture.Analyze("Something else", ["cat"]);

        Assert.Equal("Dog on a beach", fixture.EffectiveTitle());
        Assert.All(
            Metadata.GetEffectiveTags().Where(tag => tag.Tag != TagName.From("cat")),
            tag => Assert.Equal(UserValueOrigin.Accepted, tag.Origin));
    }

    [Fact]
    public void Analyzers_EachHaveTheirOwnActiveRun()
    {
        var (caption, _) = fixture.Analyze("Dog on a beach");
        var (ocr, _) = fixture.Analyze(null, ["text"], analyzer: "ocr");

        Assert.Same(caption, Metadata.GetActiveRun(ItemMetadataFixture.Caption));
        Assert.Same(ocr, Metadata.GetActiveRun("ocr"));
        Assert.Equal("Dog on a beach", fixture.EffectiveTitle());
    }

    [Fact]
    public void Notes_CannotComeFromAi()
    {
        var (run, _) = fixture.Analyze(null);

        Assert.Throws<ArgumentException>(() => AiFieldValue.For(run, DescriptiveField.Notes, FieldValue.FromText("x"), null, fixture.Now));
    }

    [Fact]
    public void ListField_NeedsListValue()
    {
        Assert.Throws<ArgumentException>(() => Metadata.EditValue(DescriptiveField.Objects, FieldValue.FromText("dog"), fixture.Now));

        Metadata.EditValue(DescriptiveField.Objects, FieldValue.FromList(["dog", " Dog ", "ball", ""]), fixture.Now);

        Assert.Equal(["dog", "ball"], Metadata.GetEffectiveValue(DescriptiveField.Objects)?.Value.Items);
    }
}
