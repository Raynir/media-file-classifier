using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for tag suggestions (E06-S07-T04): <see cref="ItemMetadata.ApplyRun"/> flags a new AI tag the user doesn't
/// have and an accepted tag the AI dropped.
/// </summary>
public sealed class TagSuggestionTests
{
    private readonly ItemMetadataFixture fixture = new();

    private ItemMetadata Metadata => fixture.Metadata;

    [Fact]
    public void FirstRun_SuggestsNothing()
    {
        fixture.Analyze("Title", ["cat", "dog"]);

        Assert.Empty(Metadata.TagSuggestions);
        Assert.False(Metadata.HasNewAiSuggestions);
    }

    [Fact]
    public void Reanalysis_NewTag_IsAdded_ButNotOneTheUserOwnsOrRejected()
    {
        fixture.Analyze("Title", ["cat"]);
        Metadata.AddTag(TagName.From("bird"), fixture.Tick());
        Metadata.RejectTag(TagName.From("car"), fixture.Tick());

        var (run, _) = fixture.Analyze("Title", ["cat", "dog", "bird", "car"]);

        var suggestion = Assert.Single(Metadata.TagSuggestions);
        Assert.Equal(new TagSuggestion(fixture.ItemId, TagName.From("dog"), run.Id, TagSuggestionKind.Added), suggestion);
        Assert.True(Metadata.HasNewAiSuggestions);
    }

    [Fact]
    public void Reanalysis_AcceptedTagDropped_IsRemoved_AndAKeptOneIsNot()
    {
        fixture.Analyze("Title", ["cat", "dog"]);
        Metadata.AcceptTag(TagName.From("cat"), fixture.Tick());
        Metadata.AcceptTag(TagName.From("dog"), fixture.Tick());

        fixture.Analyze("Title", ["dog"]);

        var suggestion = Assert.Single(Metadata.TagSuggestions);
        Assert.Equal((TagName.From("cat"), TagSuggestionKind.Removed), (suggestion.Tag, suggestion.Kind));
    }

    [Fact]
    public void Reanalysis_EditedTagMissing_IsNotRemoved()
    {
        fixture.Analyze("Title", ["cat"]);
        Metadata.AddTag(TagName.From("mine"), fixture.Tick());

        fixture.Analyze("Title", ["cat"]);

        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void Reanalysis_AddedStillGivenAndNotDealtWith_StaysFlagged_UntilTheRunDropsIt()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);

        var (third, _) = fixture.Analyze("Title", ["cat", "dog"]);
        Assert.Equal(third.Id, Assert.Single(Metadata.TagSuggestions).RunId);

        fixture.Analyze("Title", ["cat"]);
        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void Reanalysis_DismissedAdded_IsNotFlaggedAgainWhileTheRunsKeepIt()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);
        Metadata.DismissTagSuggestion(TagName.From("dog"));

        fixture.Analyze("Title", ["cat", "dog"]);

        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void Reanalysis_OfAnotherAnalyzer_KeepsTheFirstAnalyzersSuggestions()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);

        fixture.Analyze(null, ["text"], analyzer: "ocr");

        Assert.Equal(TagName.From("dog"), Assert.Single(Metadata.TagSuggestions).Tag);
    }

    [Fact]
    public void AcceptTagSuggestion_Added_AcceptsTheTag()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);

        Metadata.AcceptTagSuggestion(TagName.From("dog"), fixture.Tick());

        Assert.Empty(Metadata.TagSuggestions);
        Assert.Contains(Metadata.GetEffectiveTags(), tag => tag.Tag == TagName.From("dog") && tag.Source == MetadataSource.User);
    }

    [Fact]
    public void AcceptTagSuggestion_Removed_RemovesTheTagWithoutATombstone()
    {
        fixture.Analyze("Title", ["cat"]);
        Metadata.AcceptTag(TagName.From("cat"), fixture.Tick());
        fixture.Analyze("Title", []);

        Metadata.AcceptTagSuggestion(TagName.From("cat"), fixture.Tick());

        Assert.Empty(Metadata.TagSuggestions);
        Assert.Empty(fixture.EffectiveTags());
        Assert.Empty(Metadata.Rejections);
    }

    [Fact]
    public void AcceptTagSuggestion_NoSuggestion_Throws()
    {
        fixture.Analyze("Title", ["cat"]);

        Assert.Throws<InvalidOperationException>(() => Metadata.AcceptTagSuggestion(TagName.From("cat"), fixture.Tick()));
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("add")]
    [InlineData("reject")]
    public void UserActionOnTheTag_ClearsItsSuggestion(string action)
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);
        var dog = TagName.From("dog");

        switch (action)
        {
            case "accept":
                Metadata.AcceptTag(dog, fixture.Tick());
                break;
            case "add":
                Metadata.AddTag(dog, fixture.Tick());
                break;
            default:
                Metadata.RejectTag(dog, fixture.Tick());
                break;
        }

        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void RejectActiveRun_ClearsItsTagSuggestions()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);

        Metadata.RejectActiveRun(ItemMetadataFixture.Caption, fixture.Tick());

        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void FieldSuggestion_CountsForHasNewAiSuggestions()
    {
        fixture.Analyze("Title");
        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());

        fixture.Analyze("Other title");

        Assert.Empty(Metadata.TagSuggestions);
        Assert.True(Metadata.HasNewAiSuggestions);
    }

    [Fact]
    public void RemoveTag_DropsTheUserTagWithoutATombstone_SoAnAiTagShowsAgain()
    {
        fixture.Analyze("Title", ["dog"]);
        Metadata.AcceptTag(TagName.From("dog"), fixture.Tick());

        Assert.True(Metadata.RemoveTag(TagName.From("dog")));

        Assert.Empty(Metadata.Rejections);
        Assert.Contains(Metadata.GetEffectiveTags(), tag => tag.Tag == TagName.From("dog") && tag.Source == MetadataSource.AI);
    }

    [Fact]
    public void RemoveTag_ThatTheUserDoesNotHave_ChangesNothing()
    {
        fixture.Analyze("Title", ["dog"]);

        Assert.False(Metadata.RemoveTag(TagName.From("dog")));
        Assert.False(Metadata.RemoveTag(TagName.From("cat")));
    }

    [Fact]
    public void Reanalysis_DismissedRemovedSuggestion_DoesNotComeBack_WhileTheRunsKeepDroppingTheTag()
    {
        fixture.Analyze("Title", ["cat", "dog"]);
        Metadata.AcceptTag(TagName.From("cat"), fixture.Tick());
        fixture.Analyze("Title", ["dog"]);
        Metadata.DismissTagSuggestion(TagName.From("cat"));

        fixture.Analyze("Title", ["dog"]);

        Assert.Empty(Metadata.TagSuggestions);
    }

    [Fact]
    public void Reanalysis_DismissedAddedSuggestion_DoesNotComeBack_UntilTheRunGivesItAfterDroppingIt()
    {
        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);
        Metadata.DismissTagSuggestion(TagName.From("dog"));

        fixture.Analyze("Title", ["cat", "dog"]);
        Assert.Empty(Metadata.TagSuggestions);

        fixture.Analyze("Title", ["cat"]);
        fixture.Analyze("Title", ["cat", "dog"]);
        Assert.Equal(TagSuggestionKind.Added, Assert.Single(Metadata.TagSuggestions).Kind);
    }

    [Fact]
    public void Reanalysis_AnotherAnalyzersPendingFlag_IsNotClearedByARunThatGivesTheUserValue_NorMakesADismissedValueNew()
    {
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Mine"), fixture.Tick());
        fixture.Analyze("Other", analyzer: "other");
        Metadata.DismissSuggestion(DescriptiveField.Title);
        var (caption, _) = fixture.Analyze("Caption");

        // The other analyzer gives the user's own value: that clears nothing of the caption analyzer's flag.
        fixture.Analyze("Mine", analyzer: "other");
        Assert.Equal(caption.Id, Metadata.GetUserValue(DescriptiveField.Title)?.SuggestionRunId);

        // A different value from the other analyzer is a suggestion; once dismissed, the caption analyzer's newer pending
        // flag doesn't make the same value new when the other analyzer gives it again.
        fixture.Analyze("Other", analyzer: "other");
        Metadata.DismissSuggestion(DescriptiveField.Title);
        var (caption2, _) = fixture.Analyze("Caption two");
        fixture.Analyze("Other", analyzer: "other");
        Assert.Equal(caption2.Id, Metadata.GetUserValue(DescriptiveField.Title)?.SuggestionRunId);
    }
}
