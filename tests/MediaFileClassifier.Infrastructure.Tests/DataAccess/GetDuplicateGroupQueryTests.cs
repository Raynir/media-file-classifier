using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Duplicates.Review;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="GetDuplicateGroupQueryHandler"/> against a real library (E12-S03-T02, T03): what the
/// comparison shows of each member and which member is suggested.
/// </summary>
public sealed class GetDuplicateGroupQueryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = LibraryTestBed.Start;

    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Handle_ReturnsEveryMemberWithItsAttributes()
    {
        var items = await GroupOfThreeAsync();
        var group = await OnlyGroupAsync();

        var detail = await Handler().Handle(new GetDuplicateGroupQuery(group.Id), Token);

        Assert.NotNull(detail);
        Assert.Equal(DuplicateGroupType.Exact, detail.Type);
        Assert.Equal(3, detail.Members.Count);
        var first = detail.Members.Single(member => member.ItemId == items[0].Id);
        Assert.Equal("IMG_00001.jpg", first.FileName);
        Assert.Equal("jpg", first.Format);
        Assert.Equal(1001, first.SizeBytes);
        Assert.Equal(items[0].FileCreatedAt, first.FileCreatedAt);
        Assert.True(first.IsAvailable);
    }

    [Fact]
    public async Task Handle_CountsTagsEditsAndFavorites_AndSuggestsTheMemberWithMostOfThem()
    {
        var items = await GroupOfThreeAsync();
        var group = await OnlyGroupAsync();
        await bed.Library.Metadata.UpdateAsync(items[0].Id, metadata => metadata.AddTag(TagName.From("beach"), Now), Token);
        await bed.Library.Metadata.UpdateAsync(items[0].Id, metadata => metadata.AddTag(TagName.From("summer"), Now), Token);
        await bed.Library.Items.SetFavoriteAsync([items[0].Id], true, Now, Token);

        var detail = await Handler().Handle(new GetDuplicateGroupQuery(group.Id), Token);

        var tagged = detail!.Members.Single(member => member.ItemId == items[0].Id);
        Assert.Equal(2, tagged.TagCount);
        Assert.True(tagged.IsFavorite);
        Assert.Equal(3, tagged.UserMetadataCount);
        Assert.Equal(items[0].Id, detail.Suggestion!.ItemId);
        Assert.Equal(KeeperRule.MostUserMetadata, detail.Suggestion.DecidingRule);
    }

    [Fact]
    public async Task Handle_WithoutMetadata_FallsThroughToTheLargestFile_AndHonoursTheSavedOrder()
    {
        var items = await GroupOfThreeAsync();
        var group = await OnlyGroupAsync();

        var byDefault = await Handler().Handle(new GetDuplicateGroupQuery(group.Id), Token);
        var oldestFirst = await Handler().Handle(new GetDuplicateGroupQuery(group.Id, [KeeperRule.Oldest]), Token);

        Assert.Equal(items[2].Id, byDefault!.Suggestion!.ItemId);
        Assert.Equal(KeeperRule.LargestFile, byDefault.Suggestion.DecidingRule);

        // Item 3 has the earliest creation time, so with Oldest first it is chosen by that rule.
        Assert.Equal(items[2].Id, oldestFirst!.Suggestion!.ItemId);
        Assert.Equal(KeeperRule.Oldest, oldestFirst.Suggestion.DecidingRule);
    }

    [Fact]
    public async Task Handle_UnknownGroup_ReturnsNull()
    {
        var detail = await Handler().Handle(new GetDuplicateGroupQuery(Id<DuplicateGroup>.New()), Token);

        Assert.Null(detail);
    }

    private GetDuplicateGroupQueryHandler Handler() => new(new FixedSession(bed.Library));

    private async Task<DuplicateGroup> OnlyGroupAsync()
    {
        while (await bed.Library.Duplicates.ReconcileAsync(100, Now, Token) is { Hashes: > 0 })
        {
        }

        return Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
    }

    private async Task<List<MediaItem>> GroupOfThreeAsync()
    {
        List<MediaItem> items = [];
        for (var number = 1; number <= 3; number++)
        {
            var item = bed.NewItem(number);
            item.SetHashes(item.Hashes with { ContentHash = 10 });
            items.Add(item);
        }

        await bed.Library.Items.AddRangeAsync(items, Token);
        return items;
    }

    private sealed class FixedSession(IOpenLibrary library) : ILibrarySession
    {
        public event EventHandler? CurrentChanged
        {
            add { }
            remove { }
        }

        public IOpenLibrary? Current { get; } = library;

        public Task SetCurrentAsync(IOpenLibrary? library) => throw new NotSupportedException();
    }
}
