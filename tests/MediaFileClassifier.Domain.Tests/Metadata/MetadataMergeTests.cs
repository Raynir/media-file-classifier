using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for <see cref="MetadataMerge"/>: each merge rule of duplicate resolution (E12-S04-T02, spec 1.8), conflicts included.
/// </summary>
public sealed class MetadataMergeTests
{
    private static readonly DescriptiveField Title = DescriptiveField.Title;
    private static readonly DescriptiveField Notes = DescriptiveField.Notes;

    private readonly ItemMetadataFixture keeperFixture = new();
    private readonly ItemMetadata first = new(Id<MediaItem>.New());
    private readonly ItemMetadata second = new(Id<MediaItem>.New());

    private ItemMetadata Keeper => keeperFixture.Metadata;

    private static DateTimeOffset Now => ItemMetadataFixture.Start;

    [Fact]
    public void Apply_UserTags_AreCombinedOnTheKeeperOnce()
    {
        Keeper.AddTag(TagName.From("beach"), Now);
        first.AddTag(TagName.From("beach"), Now);
        first.AddTag(TagName.From("dog"), Now);
        second.AddTag(TagName.From("dog"), Now);
        second.AddTag(TagName.From("summer"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first, second], Now);

        Assert.Equal(["dog", "summer"], outcome.AddedTags.Select(tag => tag.Value));
        Assert.Equal(["beach", "dog", "summer"], keeperFixture.EffectiveTags());
        Assert.All(Keeper.Tags, tag => Assert.Equal(MetadataSource.User, tag.Source));
    }

    [Fact]
    public void Apply_AUserTagTheKeeperRejected_IsAddedAndTheRejectionDropped()
    {
        keeperFixture.Analyze(null, ["dog"]);
        Keeper.RejectTag(TagName.From("dog"), keeperFixture.Tick());
        first.AddTag(TagName.From("dog"), Now);

        MetadataMerge.Apply(Keeper, [first], Now);

        Assert.Equal(["dog"], keeperFixture.EffectiveTags());
        Assert.Empty(Keeper.Rejections);
    }

    [Fact]
    public void Apply_KeeperValue_WinsAndEachOtherValueIsAConflictOnce()
    {
        Keeper.EditValue(Title, FieldValue.FromText("Rex"), Now);
        first.EditValue(Title, FieldValue.FromText("Max"), Now);
        second.EditValue(Title, FieldValue.FromText("Max"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first, second], Now);

        Assert.Equal("Rex", keeperFixture.EffectiveTitle());
        Assert.Empty(outcome.CopiedFields);
        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal(Title, conflict.Field);
        Assert.Equal(FieldValue.FromText("Rex"), conflict.KeeperValue);
        Assert.Equal([new MemberFieldValue(first.ItemId, FieldValue.FromText("Max"))], conflict.Alternatives);
    }

    [Fact]
    public void Apply_SameValueOnEveryMember_IsNoConflict()
    {
        Keeper.EditValue(Title, FieldValue.FromText("Rex"), Now);
        first.EditValue(Title, FieldValue.FromText("Rex"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first], Now);

        Assert.Empty(outcome.Conflicts);
        Assert.False(outcome.Changed);
    }

    [Fact]
    public void Apply_FieldOnlyAMemberHas_IsCopiedAsAnEditedValue()
    {
        first.EditValue(Notes, FieldValue.FromText("Grandma's garden"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first], Now);

        Assert.Equal([Notes], outcome.CopiedFields);
        Assert.Empty(outcome.Conflicts);
        var notes = Keeper.GetUserValue(Notes);
        Assert.Equal("Grandma's garden", notes?.Value.Text);
        Assert.Equal(UserValueOrigin.Edited, notes?.Origin);
        Assert.Equal(Keeper.ItemId, notes?.ItemId);
    }

    [Fact]
    public void Apply_FieldOnlyMembersHaveWithDifferentValues_TakesTheFirstAndReportsTheOthers()
    {
        first.EditValue(Title, FieldValue.FromText("Max"), Now);
        second.EditValue(Title, FieldValue.FromText("Bella"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first, second], Now);

        Assert.Equal("Max", keeperFixture.EffectiveTitle());
        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal(FieldValue.FromText("Max"), conflict.KeeperValue);
        Assert.Equal([new MemberFieldValue(second.ItemId, FieldValue.FromText("Bella"))], conflict.Alternatives);
    }

    [Fact]
    public void Apply_AcceptedValueOfAMember_TakesPartAsAUserValue()
    {
        var memberFixture = new ItemMetadataFixture();
        memberFixture.Analyze("Dog on a beach");
        memberFixture.Metadata.AcceptAiValue(Title, memberFixture.Tick());
        Keeper.EditValue(Title, FieldValue.FromText("Rex"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [memberFixture.Metadata], Now);

        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal(FieldValue.FromText("Dog on a beach"), Assert.Single(conflict.Alternatives).Value);
    }

    [Fact]
    public void Apply_AiValuesAndTagsOfMembers_AreNotMerged()
    {
        var memberFixture = new ItemMetadataFixture();
        memberFixture.Analyze("Dog on a beach", ["dog"]);
        keeperFixture.Analyze("A dog", ["animal"]);

        var outcome = MetadataMerge.Apply(Keeper, [memberFixture.Metadata], Now);

        Assert.False(outcome.Changed);
        Assert.Empty(outcome.Conflicts);
        Assert.Equal("A dog", keeperFixture.EffectiveTitle());
        Assert.Equal(["animal"], keeperFixture.EffectiveTags());
        Assert.Empty(Keeper.UserValues);
    }

    [Fact]
    public void Apply_KeeperWithAnAiValue_TakesAMembersUserValueOverIt()
    {
        keeperFixture.Analyze("A dog");
        first.EditValue(Title, FieldValue.FromText("Rex"), Now);

        var outcome = MetadataMerge.Apply(Keeper, [first], Now);

        Assert.Equal([Title], outcome.CopiedFields);
        Assert.Equal("Rex", keeperFixture.EffectiveTitle());
    }

    [Fact]
    public void Apply_Members_AreNotChanged()
    {
        Keeper.AddTag(TagName.From("beach"), Now);
        Keeper.EditValue(Title, FieldValue.FromText("Rex"), Now);
        first.EditValue(Notes, FieldValue.FromText("garden"), Now);
        var before = first.CaptureUserLayer();

        MetadataMerge.Apply(Keeper, [first], Now);

        Assert.Equal(before.UserValues, first.CaptureUserLayer().UserValues);
        Assert.Empty(first.Tags);
    }

    [Fact]
    public void Apply_KeeperAmongTheMembers_Throws()
    {
        Assert.Throws<ArgumentException>(() => MetadataMerge.Apply(Keeper, [first, Keeper], Now));
    }

    [Fact]
    public void Apply_ThenRestoreUserLayer_PutsTheKeeperBack()
    {
        Keeper.EditValue(Title, FieldValue.FromText("Rex"), Now);
        var before = Keeper.CaptureUserLayer();
        first.AddTag(TagName.From("dog"), Now);
        first.EditValue(Notes, FieldValue.FromText("garden"), Now);
        MetadataMerge.Apply(Keeper, [first], Now);

        Keeper.RestoreUserLayer(before);

        Assert.Empty(keeperFixture.EffectiveTags());
        Assert.Null(Keeper.GetUserValue(Notes));
        Assert.Equal("Rex", keeperFixture.EffectiveTitle());
    }
}
