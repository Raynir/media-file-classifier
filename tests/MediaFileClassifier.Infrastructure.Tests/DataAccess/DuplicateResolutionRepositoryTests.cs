using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using MediaFileClassifier.UseCases.Duplicates;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="DuplicateResolutionRepository"/> and library schema 21 against a real library database (E12-S04).
/// </summary>
public sealed class DuplicateResolutionRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IDuplicateResolutionRepository Log => bed.Library.DuplicateResolutions;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Add_WithMergeRecord_RoundTripsAndSetsTheGroupState()
    {
        var (group, items) = await GroupAsync();
        var record = new MetadataMergeRecord(
            [TagName.From("beach"), TagName.From("dog")],
            [TagName.From("dog")],
            [
                new MergedFieldChange(DescriptiveField.Title, null, FieldValue.FromText("Rex").WithOtherForm("Рекс")),
                new MergedFieldChange(DescriptiveField.People, FieldValue.FromList(["Ann"]), FieldValue.FromList(["Ann", "Bob"])),
            ],
            FavoriteAdded: true);
        var resolution = DuplicateResolution.Resolved(group, items[0].Id, DuplicateResolutionOptions.Default, record, LibraryTestBed.Start);
        var changed = 0;
        bed.Library.Duplicates.Changed += (_, _) => changed++;

        var added = await Log.AddAsync(resolution, Members(resolution, items), DuplicateGroupState.Resolved, Token);

        Assert.True(added);
        Assert.Equal(1, changed);
        Assert.Equal(DuplicateGroupState.Resolved, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
        var entry = (await Log.GetAsync(resolution.Id, Token))!;
        Assert.Equal(items[0].Id, entry.Resolution.KeeperId);
        Assert.True(entry.Resolution.MergedMetadata && entry.Resolution.RemovedFromLibrary && !entry.Resolution.TrashRequested);
        Assert.Equal(LibraryTestBed.Start, entry.Resolution.CreatedAt);
        var stored = entry.Resolution.Merge!;
        Assert.Equal(record.AddedTags, stored.AddedTags);
        Assert.Equal(record.UnrejectedTags, stored.UnrejectedTags);
        Assert.Equal(record.Fields, stored.Fields);
        Assert.Equal("Рекс", stored.Fields[0].After.OtherForm);
        Assert.True(stored.FavoriteAdded);
        Assert.Equal(DuplicateMemberAction.Kept, entry.Members[0].Action);
    }

    [Fact]
    public async Task Add_GroupGone_WritesNothing()
    {
        var (group, items) = await GroupAsync();
        var stranger = new DuplicateGroup(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, DuplicateGroupState.Open, 99, LibraryTestBed.Start, LibraryTestBed.Start);
        var resolution = DuplicateResolution.Ignored(stranger, LibraryTestBed.Start);

        var added = await Log.AddAsync(resolution, Members(resolution, items), DuplicateGroupState.Ignored, Token);

        Assert.False(added);
        Assert.Equal(0, await Log.CountAsync(Token));
        Assert.Equal(DuplicateGroupState.Open, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
    }

    [Fact]
    public async Task UpdateMembers_ChangesActionAndReason()
    {
        var (group, items) = await GroupAsync();
        var resolution = DuplicateResolution.Resolved(group, items[0].Id, new(MoveToTrash: true), null, LibraryTestBed.Start);
        await Log.AddAsync(resolution, Members(resolution, items), DuplicateGroupState.Resolved, Token);

        await Log.UpdateMembersAsync([new DuplicateResolutionMember(resolution.Id, items[1].Id, "x", DuplicateMemberAction.TrashSkipped, "Read-only")], Token);

        var member = (await Log.GetAsync(resolution.Id, Token))!.Members.Single(row => row.ItemId == items[1].Id);
        Assert.Equal(DuplicateMemberAction.TrashSkipped, member.Action);
        Assert.Equal("Read-only", member.Reason);
        Assert.Equal(@"D:\Photos\2.jpg", member.Path);
    }

    [Fact]
    public async Task GetPage_NewestFirst_AndMarkUndoneOnce()
    {
        var (group, items) = await GroupAsync();
        var older = DuplicateResolution.Ignored(group, LibraryTestBed.Start);
        await Log.AddAsync(older, Members(older, items), DuplicateGroupState.Ignored, Token);
        var newer = DuplicateResolution.Ignored(group, LibraryTestBed.Start.AddMinutes(1));
        await Log.AddAsync(newer, Members(newer, items), DuplicateGroupState.Ignored, Token);

        var page = await Log.GetPageAsync(0, 10, Token);
        var first = await Log.MarkUndoneAsync(older.Id, DuplicateGroupState.Open, LibraryTestBed.Start.AddMinutes(2), Token);
        var second = await Log.MarkUndoneAsync(older.Id, DuplicateGroupState.Open, LibraryTestBed.Start.AddMinutes(3), Token);

        Assert.Equal([newer.Id, older.Id], page.Select(entry => entry.Resolution.Id));
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(LibraryTestBed.Start.AddMinutes(2), (await Log.GetAsync(older.Id, Token))!.Resolution.UndoneAt);
        Assert.Equal(DuplicateGroupState.Open, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
    }

    private static List<DuplicateResolutionMember> Members(DuplicateResolution resolution, IReadOnlyList<MediaItem> items) =>
        [.. items.Select((item, index) => new DuplicateResolutionMember(
            resolution.Id,
            item.Id,
            $@"D:\Photos\{index + 1}.jpg",
            index == 0 ? DuplicateMemberAction.Kept : DuplicateMemberAction.Removed))];

    private async Task<(DuplicateGroup Group, IReadOnlyList<MediaItem> Items)> GroupAsync()
    {
        var items = Enumerable.Range(1, 2).Select(number =>
        {
            var item = bed.NewItem(number);
            item.SetHashes(new FileHashes { ContentHash = 10 });
            return item;
        }).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        while ((await bed.Library.Duplicates.ReconcileAsync(DuplicateGroupMaintainer.BatchSize, LibraryTestBed.Start, Token)).Hashes > 0)
        {
        }

        return (Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token)), items);
    }
}
