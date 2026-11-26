using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="VisualDuplicateRepository"/> and the triggers of library schema 21 against a real library
/// database (E12-S02).
/// </summary>
public sealed class VisualDuplicateRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IVisualDuplicateRepository Visual => bed.Library.VisualDuplicates;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Triggers_PerceptualHashComputed_QueuesTheItem()
    {
        var items = await AddAsync((null, null), (null, null));

        await SetHashesAsync(items[0], perceptual: 0xF0F0, content: 1);

        Assert.Equal([items[0].Id], (await Visual.PeekQueueAsync(10, Token)).Items);
    }

    [Fact]
    public async Task Triggers_ItemGoesMissing_QueuesItAndItsExactCopies()
    {
        var items = await AddAsync((0xF0F0, 1), (0xF0F0, 1), (0xFF, 2));
        await ClearQueueAsync();

        items[0].ChangeStatus(MediaItemStatus.Missing, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[0], Token);

        Assert.Equal(Ids(items[0], items[1]), Ids((await Visual.PeekQueueAsync(10, Token)).Items));
    }

    [Fact]
    public async Task Triggers_WholeItemEmbeddingWritten_QueuesTheItem()
    {
        var items = await AddAsync((0xF0F0, 1));
        await ClearQueueAsync();

        await bed.Library.Embeddings.ReplaceAsync(items[0].Id, bed.Model.Id, [Embedding.Create(items[0].Id, bed.Model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick())], Token);

        Assert.Equal([items[0].Id], (await Visual.PeekQueueAsync(10, Token)).Items);
    }

    [Fact]
    public async Task Triggers_KeyframesWritten_QueueTheItem()
    {
        var items = await AddAsync((null, 1));
        await ClearQueueAsync();

        await bed.Library.Keyframes.ReplaceAsync(items[0].Id, [new Keyframe(Id<Keyframe>.New(), items[0].Id, 0, TimeSpan.Zero, 0, 0xABCD)], Token);

        Assert.Equal([items[0].Id], (await Visual.PeekQueueAsync(10, Token)).Items);
    }

    [Fact]
    public async Task Triggers_GroupMemberDeleted_QueuesTheOtherMembers()
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3));
        await ApplyAsync([items[0].Id, items[1].Id, items[2].Id], [[items[0].Id, items[1].Id, items[2].Id]]);
        await ClearQueueAsync();

        await bed.Library.Items.DeleteAsync(items[0].Id, Token);

        Assert.Equal(Ids(items[1], items[2]), Ids((await Visual.PeekQueueAsync(10, Token)).Items));
    }

    [Fact]
    public async Task Apply_NewGroup_IsAnOpenVisualGroup()
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3));

        var result = await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[2].Id]]);

        Assert.Equal(1, result.Created);
        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(DuplicateGroupType.Visual, group.Type);
        Assert.Equal(DuplicateGroupState.Open, group.State);
        Assert.Null(group.ContentHash);
        Assert.Equal(Ids(items[0], items[2]), await MemberIdsAsync(group.Id));
    }

    [Theory]
    [InlineData(DuplicateGroupState.Ignored)]
    [InlineData(DuplicateGroupState.Resolved)]
    public async Task Apply_GroupGainsAMember_KeepsItsIdAndOpensAgain(DuplicateGroupState state)
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3));
        await ApplyAsync([items[0].Id, items[1].Id], [[items[0].Id, items[1].Id]]);
        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        await bed.Library.Duplicates.ChangeStateAsync(group.Id, state, bed.Clock.Tick(), Token);

        var result = await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[1].Id, items[2].Id]]);

        Assert.Equal((0, 1, 1), (result.Created, result.Changed, result.Reopened));
        var kept = (await bed.Library.Duplicates.GetAsync(group.Id, Token))!;
        Assert.Equal(DuplicateGroupState.Open, kept.State);
        Assert.Equal(Ids(items[0], items[1], items[2]), await MemberIdsAsync(group.Id));
    }

    [Fact]
    public async Task Apply_IgnoredGroupLosesAMember_StaysIgnored()
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3));
        await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[1].Id, items[2].Id]]);
        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        await bed.Library.Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, bed.Clock.Tick(), Token);

        await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[1].Id]]);

        Assert.Equal(DuplicateGroupState.Ignored, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
        Assert.Equal(Ids(items[0], items[1]), await MemberIdsAsync(group.Id));
    }

    [Fact]
    public async Task Apply_GroupSplits_TheLargerPartKeepsTheGroup()
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3), (4, 4), (5, 5));
        await ApplyAsync([.. items.Select(item => item.Id)], [[.. items.Select(item => item.Id)]]);
        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));

        var result = await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[1].Id], [items[2].Id, items[3].Id, items[4].Id]]);

        Assert.Equal((1, 1, 0), (result.Created, result.Changed, result.Deleted));
        Assert.Equal(Ids(items[2], items[3], items[4]), await MemberIdsAsync(group.Id));
    }

    [Fact]
    public async Task Apply_NoGroupLeftInTheScope_DeletesTheOldGroup()
    {
        var items = await AddAsync((1, 1), (2, 2));
        await ApplyAsync([items[0].Id, items[1].Id], [[items[0].Id, items[1].Id]]);

        var result = await ApplyAsync([items[0].Id], []);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(0, await bed.Library.Duplicates.CountAsync(null, Token));
    }

    [Fact]
    public async Task Apply_MemberWentMissingSinceMatching_IsLeftOut()
    {
        var items = await AddAsync((1, 1), (2, 2), (3, 3));
        items[2].ChangeStatus(MediaItemStatus.Missing, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[2], Token);

        await ApplyAsync([.. items.Select(item => item.Id)], [[items[0].Id, items[1].Id, items[2].Id], [items[2].Id, Id<MediaItem>.New()]]);

        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(Ids(items[0], items[1]), await MemberIdsAsync(group.Id));
    }

    [Fact]
    public async Task Apply_ItemQueuedAgainAfterThePeek_StaysQueued()
    {
        var items = await AddAsync((1, 1), (2, 2));
        var batch = await Visual.PeekQueueAsync(10, Token);
        await SetHashesAsync(items[0], perceptual: 99, content: 1);

        var result = await Visual.ApplyAsync(new VisualGroupUpdate([.. items.Select(item => item.Id)], [], batch), bed.Clock.Tick(), Token);

        Assert.Equal(1, result.Items);
        Assert.Equal([items[0].Id], (await Visual.PeekQueueAsync(10, Token)).Items);
    }

    [Fact]
    public async Task QueueAll_QueuesItemsWithHashesOrKeyframes()
    {
        var items = await AddAsync((1, 1), (null, 2), (null, 3));
        await bed.Library.Keyframes.ReplaceAsync(items[2].Id, [new Keyframe(Id<Keyframe>.New(), items[2].Id, 0, TimeSpan.Zero, 0, 0xABCD)], Token);
        await ClearQueueAsync();

        Assert.Equal(2, await Visual.QueueAllAsync(Token));
        Assert.Equal(Ids(items[0], items[2]), Ids((await Visual.PeekQueueAsync(10, Token)).Items));
    }

    [Fact]
    public async Task GetCandidates_LeavesOutMissingItemsAndItemsWithoutHashes()
    {
        var items = await AddAsync((1, 10), (2, 20), (null, 30));
        items[1].ChangeStatus(MediaItemStatus.Excluded, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[1], Token);

        var candidate = Assert.Single(await Visual.GetCandidatesAsync(null, Token));

        Assert.Equal((items[0].Id, MediaType.Image, (UInt128?)10, (ulong?)1, 0, (TimeSpan?)null), (candidate.ItemId, candidate.MediaType, candidate.ContentHash, candidate.PerceptualHash, candidate.FrameHashes.Count, candidate.Duration));
    }

    private static IEnumerable<string> Ids(params MediaItem[] items) => items.Select(item => item.Id.ToString()).Order(StringComparer.Ordinal);

    private static IEnumerable<string> Ids(IEnumerable<Id<MediaItem>> items) => items.Select(item => item.ToString()).Order(StringComparer.Ordinal);

    private async Task<IEnumerable<string>> MemberIdsAsync(Id<DuplicateGroup> group) =>
        (await bed.Library.Duplicates.GetMembersAsync(group, Token)).Select(member => member.ItemId.ToString()).Order(StringComparer.Ordinal);

    private async Task<List<MediaItem>> AddAsync(params (ulong? Perceptual, UInt128? Content)[] hashes)
    {
        var offset = await bed.Library.Items.CountAsync(Token);
        var items = hashes.Select((hash, index) =>
        {
            var item = bed.NewItem(offset + index + 1);
            item.SetHashes(item.Hashes with { PerceptualHash = hash.Perceptual, ContentHash = hash.Content });
            return item;
        }).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        return items;
    }

    private async Task SetHashesAsync(MediaItem item, ulong perceptual, UInt128 content)
    {
        item.SetHashes(item.Hashes with { PerceptualHash = perceptual, ContentHash = content });
        await bed.Library.Items.UpdateAsync(item, Token);
    }

    private Task<VisualReconcileResult> ApplyAsync(IReadOnlyCollection<Id<MediaItem>> scope, IReadOnlyList<IReadOnlyCollection<Id<MediaItem>>> groups) =>
        Visual.ApplyAsync(new VisualGroupUpdate(scope, groups, VisualQueueBatch.Empty), bed.Clock.Tick(), Token);

    private async Task ClearQueueAsync()
    {
        await using var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "duplicate_dirty_item" """, Token);
    }
}
