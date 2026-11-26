using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Metadata;
using MediaFileClassifier.UseCases.Duplicates.MergeMetadata;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="MergeMetadataCommand"/> (E12-S04-T02) against a real library: what reaches the keeper, the favorite
/// mark, the notifications, the tag limit and the undo snapshot.
/// </summary>
public sealed class MergeMetadataFlowTests : IAsyncLifetime
{
    private static readonly DescriptiveField Title = DescriptiveField.Title;

    private MetadataTestBed bed = null!;
    private MediaItem member = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private MediaItem Keeper => bed.Item;

    public async ValueTask InitializeAsync()
    {
        bed = await MetadataTestBed.CreateAsync();
        member = await bed.AddItemAsync(2);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Merge_TagsFieldsAndFavorite_ReachTheKeeperAndAreSearchable()
    {
        await EditAsync(Keeper, new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")), new MetadataEdit.AddTag("beach"));
        await EditAsync(member, new MetadataEdit.SetField(Title, FieldValue.FromText("Max")), new MetadataEdit.AddTag("terrier"));
        await bed.Open.Items.SetFavoriteAsync([member.Id], true, bed.Library.Clock.GetUtcNow(), Token);
        bed.Published.Clear();

        var result = await MergeAsync(member.Id);

        var keeper = await bed.GetAsync();
        Assert.Equal(["beach", "terrier"], keeper.GetEffectiveTags().Select(tag => tag.Tag.Value).Order(StringComparer.Ordinal));
        Assert.Equal("Rex", keeper.GetEffectiveValue(Title)?.Value.Text);
        Assert.Equal(FieldValue.FromText("Max"), Assert.Single(Assert.Single(result.Outcome.Conflicts).Alternatives).Value);
        Assert.True(result.FavoriteAdded);
        Assert.True(Assert.Single(await bed.Open.Items.GetSummariesByIdsAsync([Keeper.Id], Token)).IsFavorite);
        Assert.Equal(
            [(ItemMetadataChange.Tags, false), (ItemMetadataChange.Favorite, true)],
            bed.Published.Select(change => (change.Change, change.IsFavorite)));
        Assert.Contains(Keeper.Id, await FoundAsync("terrier"));
    }

    [Fact]
    public async Task Merge_KeeperAlreadyAFavorite_LeavesTheMarkAndPublishesNoFavoriteChange()
    {
        await bed.Open.Items.SetFavoriteAsync([Keeper.Id, member.Id], true, bed.Library.Clock.GetUtcNow(), Token);
        await EditAsync(member, new MetadataEdit.AddTag("dog"));
        bed.Published.Clear();

        var result = await MergeAsync(member.Id);

        Assert.False(result.FavoriteAdded);
        Assert.True(result.Undo?.WasFavorite);
        Assert.Equal([ItemMetadataChange.Tags], bed.Published.Select(change => change.Change));
    }

    [Fact]
    public async Task Merge_MembersWithOnlyAiValues_ChangesNothingAndKeepsNoUndo()
    {
        await bed.ApplyRunAsync("Dog on a beach", ["dog"], member);
        bed.Published.Clear();

        var result = await MergeAsync(member.Id);

        Assert.False(result.Outcome.Changed);
        Assert.Null(result.Undo);
        Assert.Empty(bed.Published);
        Assert.Empty((await bed.GetAsync()).GetEffectiveTags());
    }

    [Fact]
    public async Task Merge_UndoSnapshot_PutsTheKeeperBack()
    {
        await EditAsync(Keeper, new MetadataEdit.AddTag("beach"));
        await EditAsync(member, new MetadataEdit.SetField(Title, FieldValue.FromText("Max")), new MetadataEdit.AddTag("dog"));
        var undo = (await MergeAsync(member.Id)).Undo!;

        await bed.Open.Metadata.UpdateAsync(Keeper.Id, metadata => metadata.RestoreUserLayer(undo.UserLayer), Token);

        var keeper = await bed.GetAsync();
        Assert.Equal(["beach"], keeper.GetEffectiveTags().Select(tag => tag.Tag.Value));
        Assert.Null(keeper.GetUserValue(Title));
        Assert.Equal(Keeper.Id, undo.KeeperId);
        Assert.False(undo.WasFavorite);
    }

    [Fact]
    public async Task Merge_PastTheUserTagLimit_KeepsEveryTagAndSaysSo()
    {
        var tags = Enumerable.Range(0, MetadataLimits.UserTagsPerItem).Select(index => new MetadataEdit.AddTag($"k{index}"));
        await EditAsync(Keeper, [.. tags]);
        await EditAsync(member, new MetadataEdit.AddTag("extra"));

        var result = await MergeAsync(member.Id);

        Assert.True(result.TooManyTags);
        Assert.Equal(MetadataLimits.UserTagsPerItem + 1, (await bed.GetAsync()).GetEffectiveTags().Count);
    }

    [Fact]
    public async Task Merge_NoRealMember_ChangesNothing()
    {
        await EditAsync(Keeper, new MetadataEdit.AddTag("beach"));

        var result = await MergeAsync(Keeper.Id, Id<MediaItem>.New());

        Assert.Same(MergeMetadataResult.None, result);
    }

    [Fact]
    public async Task Merge_UnknownKeeper_ChangesNothing()
    {
        await EditAsync(member, new MetadataEdit.AddTag("dog"));

        var result = await bed.Sender.Send(new MergeMetadataCommand(Id<MediaItem>.New(), [member.Id]), Token);

        Assert.Same(MergeMetadataResult.None, result);
    }

    private Task<MergeMetadataResult> MergeAsync(params Id<MediaItem>[] memberIds) =>
        bed.Sender.Send(new MergeMetadataCommand(Keeper.Id, memberIds), Token);

    private Task<EditMetadataResult> EditAsync(MediaItem item, params MetadataEdit[] edits) =>
        bed.Sender.Send(new EditMetadataCommand([item.Id], edits), Token);

    private async Task<IReadOnlyList<Id<MediaItem>>> FoundAsync(string word) =>
        [.. (await bed.Open.Search.FindKeywordMatchesAsync(SearchStoreTests.Search(word), Token)).Matches.Select(match => match.ItemId)];
}
