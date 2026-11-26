using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="DuplicateGroupRepository"/> and the triggers of library schema 20 against a real library
/// database (E12-S01-T01, T02).
/// </summary>
public sealed class DuplicateGroupRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IDuplicateGroupRepository Duplicates => bed.Library.Duplicates;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Reconcile_ItemsSharingAContentHash_FormOneOpenExactGroup()
    {
        var items = await AddAsync(10, 10, 20, 10, null);

        var result = await ReconcileAllAsync();

        Assert.Equal(1, result.Created);
        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(DuplicateGroupType.Exact, group.Type);
        Assert.Equal(DuplicateGroupState.Open, group.State);
        Assert.Equal((UInt128)10, group.ContentHash);
        Assert.Equal(Ids(items[0], items[1], items[3]), await MemberIdsAsync(group));
        Assert.Equal(0, await Duplicates.CountQueuedAsync(Token));
    }

    [Fact]
    public async Task Reconcile_FullHashComputedLater_CreatesTheGroup()
    {
        var items = await AddAsync(10, null);
        await ReconcileAllAsync();
        Assert.Equal(0, await Duplicates.CountAsync(null, Token));

        items[1].SetHashes(items[1].Hashes with { ContentHash = 10 });
        await bed.Library.Items.UpdateAsync(items[1], Token);
        await ReconcileAllAsync();

        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(Ids(items[0], items[1]), await MemberIdsAsync(group));
    }

    [Theory]
    [InlineData(DuplicateGroupState.Ignored)]
    [InlineData(DuplicateGroupState.Resolved)]
    public async Task Reconcile_NewMemberJoinsAClosedGroup_OpensItAgain(DuplicateGroupState state)
    {
        await AddAsync(10, 10);
        await ReconcileAllAsync();
        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.True(await Duplicates.ChangeStateAsync(group.Id, state, bed.Clock.Tick(), Token));

        var third = await AddAsync(10);
        var result = await ReconcileAllAsync();

        Assert.Equal(1, result.Reopened);
        var reopened = (await Duplicates.GetAsync(group.Id, Token))!;
        Assert.Equal(DuplicateGroupState.Open, reopened.State);
        Assert.Contains(third[0].Id.ToString(), await MemberIdsAsync(reopened));
    }

    [Fact]
    public async Task Reconcile_IgnoredGroupWithoutNewMembers_StaysIgnored()
    {
        var items = await AddAsync(10, 10, 10);
        await ReconcileAllAsync();
        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        await Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, bed.Clock.Tick(), Token);

        // A member leaving, and a full pass, change nothing about the state.
        items[2].ChangeStatus(MediaItemStatus.Missing, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[2], Token);
        await ReconcileAllAsync();
        await Duplicates.QueueAllAsync(Token);
        await ReconcileAllAsync();

        var stored = (await Duplicates.GetAsync(group.Id, Token))!;
        Assert.Equal(DuplicateGroupState.Ignored, stored.State);
        Assert.Equal(Ids(items[0], items[1]), await MemberIdsAsync(stored));
        Assert.Empty(await Duplicates.GetGroupsAsync(DuplicateGroupState.Open, 0, 10, Token));
    }

    [Theory]
    [InlineData(MediaItemStatus.Missing)]
    [InlineData(MediaItemStatus.Excluded)]
    public async Task Reconcile_MemberMissingOrExcluded_LeavesAndAPairDissolves(MediaItemStatus status)
    {
        var items = await AddAsync(10, 10);
        await ReconcileAllAsync();

        items[1].ChangeStatus(status, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[1], Token);
        var result = await ReconcileAllAsync();

        Assert.Equal(1, result.Deleted);
        Assert.Equal(0, await Duplicates.CountAsync(null, Token));

        // Back again: a new Open group.
        items[1].ChangeStatus(MediaItemStatus.Pending, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[1], Token);
        await ReconcileAllAsync();
        Assert.Equal(1, await Duplicates.CountAsync(DuplicateGroupState.Open, Token));
    }

    [Fact]
    public async Task Reconcile_MemberDeleted_LeavesTheGroup()
    {
        var items = await AddAsync(10, 10, 10);
        await ReconcileAllAsync();

        await bed.Library.Items.DeleteAsync(items[0].Id, Token);
        await ReconcileAllAsync();

        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(Ids(items[1], items[2]), await MemberIdsAsync(group));
    }

    [Fact]
    public async Task Reconcile_FolderRemoved_DissolvesItsGroups()
    {
        await AddAsync(10, 10, 20, 20);
        await ReconcileAllAsync();

        while ((await bed.Library.Items.DeleteFromFolderAsync(bed.Folder.Id, 3, Token)).Count > 0)
        {
        }

        await ReconcileAllAsync();

        Assert.Equal(0, await Duplicates.CountAsync(null, Token));
    }

    [Fact]
    public async Task Reconcile_FileChanged_MovesTheItemToItsNewHashGroup()
    {
        var items = await AddAsync(10, 10, 10, 20);
        await ReconcileAllAsync();

        items[0].UpdateFile(items[0].SizeBytes + 1, items[0].FileCreatedAt, bed.Clock.Tick(), new FileHashes { ContentHash = 20 });
        await bed.Library.Items.UpdateFilesAsync([items[0]], Token);
        await ReconcileAllAsync();

        var groups = await Duplicates.GetGroupsAsync(null, 0, 10, Token);
        Assert.Equal(2, groups.Count);
        Assert.Equal(Ids(items[1], items[2]), await MemberIdsAsync(groups.Single(group => group.ContentHash == 10)));
        Assert.Equal(Ids(items[0], items[3]), await MemberIdsAsync(groups.Single(group => group.ContentHash == 20)));
    }

    [Fact]
    public async Task Reconcile_StatusChangeThatKeepsMembership_QueuesNothing()
    {
        var items = await AddAsync(10, 10);
        await ReconcileAllAsync();

        items[0].ChangeStatus(MediaItemStatus.Pending, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(items[0], Token);

        Assert.Equal(0, await Duplicates.CountQueuedAsync(Token));
    }

    [Fact]
    public async Task Reconcile_WithALimit_TakesTheQueueInBatches()
    {
        await AddAsync(1, 1, 2, 2, 3, 3);

        var first = await Duplicates.ReconcileAsync(2, bed.Clock.GetUtcNow(), Token);

        Assert.Equal(2, first.Hashes);
        Assert.Equal(1, await Duplicates.CountQueuedAsync(Token));
        Assert.Equal(1, (await Duplicates.ReconcileAsync(2, bed.Clock.GetUtcNow(), Token)).Hashes);
        Assert.Same(DuplicateReconcileResult.Empty, await Duplicates.ReconcileAsync(2, bed.Clock.GetUtcNow(), Token));
        Assert.Equal(3, await Duplicates.CountAsync(null, Token));
    }

    [Fact]
    public async Task Reconcile_ChangedGroups_RaisesChanged()
    {
        await AddAsync(10, 10);
        var raised = 0;
        Duplicates.Changed += (_, _) => raised++;

        await ReconcileAllAsync();
        await ReconcileAllAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task QueueAll_RebuildsLostGroupsAndDropsStaleOnes()
    {
        var items = await AddAsync(10, 10, 20, 20);
        await ReconcileAllAsync();
        await RunSqlAsync("""DELETE FROM "duplicate_group" WHERE "content_hash" = '0000000000000000000000000000000a'""");
        await RunSqlAsync("""UPDATE "media_item" SET "hashes_content_hash" = '00000000000000000000000000000063' WHERE "id" = '""" + items[3].Id + "'");
        await RunSqlAsync("""DELETE FROM "duplicate_dirty_hash" """);

        Assert.Equal(2, await Duplicates.QueueAllAsync(Token));
        await ReconcileAllAsync();

        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal((UInt128)10, group.ContentHash);
    }

    [Fact]
    public async Task ChangeState_NotAllowed_ThrowsAndKeepsTheState()
    {
        await AddAsync(10, 10);
        await ReconcileAllAsync();
        var group = Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token));
        await Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, bed.Clock.Tick(), Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Resolved, bed.Clock.Tick(), Token));

        Assert.Equal(DuplicateGroupState.Ignored, (await Duplicates.GetAsync(group.Id, Token))!.State);
        Assert.False(await Duplicates.ChangeStateAsync(Id<DuplicateGroup>.New(), DuplicateGroupState.Ignored, bed.Clock.Tick(), Token));
    }

    [Fact]
    public async Task Upgrade_ItemsThatAlreadyShareAHash_AreQueuedForGrouping()
    {
        await AddAsync(10, 10, 20);
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations[migrations.FindIndex(name => name.EndsWith("_DuplicateGroups", StringComparison.Ordinal)) - 1], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
        Assert.Equal(1, await Duplicates.CountQueuedAsync(Token));
        await ReconcileAllAsync();
        Assert.Equal((UInt128)10, Assert.Single(await Duplicates.GetGroupsAsync(null, 0, 10, Token)).ContentHash);
    }

    private static IEnumerable<string> Ids(params MediaItem[] items) => items.Select(item => item.Id.ToString()).Order(StringComparer.Ordinal);

    private async Task<IEnumerable<string>> MemberIdsAsync(DuplicateGroup group) =>
        (await Duplicates.GetMembersAsync(group.Id, Token)).Select(member => member.ItemId.ToString()).Order(StringComparer.Ordinal);

    private async Task<List<MediaItem>> AddAsync(params UInt128?[] hashes)
    {
        var offset = await bed.Library.Items.CountAsync(Token);
        var items = hashes.Select((hash, index) =>
        {
            var item = bed.NewItem(offset + index + 1);
            item.SetHashes(item.Hashes with { ContentHash = hash });
            return item;
        }).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        return items;
    }

    private async Task<DuplicateReconcileResult> ReconcileAllAsync()
    {
        int hashes = 0, created = 0, changed = 0, reopened = 0, deleted = 0;
        while (await Duplicates.ReconcileAsync(100, bed.Clock.Tick(), Token) is { Hashes: > 0 } result)
        {
            (hashes, created, changed, reopened, deleted) = (hashes + result.Hashes, created + result.Created, changed + result.Changed, reopened + result.Reopened, deleted + result.Deleted);
        }

        return new DuplicateReconcileResult(hashes, created, changed, reopened, deleted);
    }

    private async Task RunSqlAsync(string sql)
    {
        await using var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
        await db.Database.ExecuteSqlRawAsync(sql, Token);
    }
}
