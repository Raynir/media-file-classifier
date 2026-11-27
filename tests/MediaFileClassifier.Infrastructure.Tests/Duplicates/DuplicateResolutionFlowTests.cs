using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.UseCases.Duplicates;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using MediaFileClassifier.UseCases.Duplicates.Resolution.IgnoreDuplicateGroup;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// Duplicate resolution actions (E12-S04) against a real library and job system: ignoring, the resolution log and the
/// full detection pass afterwards.
/// </summary>
public sealed class DuplicateResolutionFlowTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Ignore_OpenGroup_IsIgnoredAndLoggedWithEveryMember()
    {
        var items = await AddCopiesAsync(3, hash: 7);
        var group = await SingleGroupAsync();

        var entryId = await bed.Sender.Send(new IgnoreDuplicateGroupCommand(group.Id), Token);

        Assert.NotNull(entryId);
        Assert.Equal(DuplicateGroupState.Ignored, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
        var entry = (await bed.Library.DuplicateResolutions.GetAsync(entryId.Value, Token))!;
        Assert.Equal(DuplicateResolutionKind.Ignored, entry.Resolution.Kind);
        Assert.Equal(group.Id, entry.Resolution.GroupId);
        Assert.Null(entry.Resolution.KeeperId);
        Assert.Equal(items.Select(item => item.Id).ToHashSet(), entry.Members.Select(member => member.ItemId).ToHashSet());
        Assert.All(entry.Members, member => Assert.Equal(DuplicateMemberAction.Ignored, member.Action));
        Assert.All(entry.Members, member => Assert.EndsWith(".jpg", member.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ignore_ThenFullDetectionPass_GroupStaysIgnoredAndIsNotSuggested()
    {
        await AddCopiesAsync(2, hash: 7);
        await AddCopiesAsync(2, hash: 8, first: 10);
        var groups = await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token);
        var ignored = groups[0];
        await bed.Sender.Send(new IgnoreDuplicateGroupCommand(ignored.Id), Token);

        var pass = await bed.Sender.Send(new DetectDuplicatesCommand(), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(DetectDuplicatesOutcome.Started, pass.Outcome);
        Assert.Equal(DuplicateGroupState.Ignored, (await bed.Library.Duplicates.GetAsync(ignored.Id, Token))!.State);
        Assert.Equal(1, await bed.Library.Duplicates.CountAsync(DuplicateGroupState.Open, Token));
        Assert.DoesNotContain(ignored.Id, (await bed.Library.Duplicates.GetSummariesAsync(DuplicateGroupState.Open, Token)).Select(summary => summary.Id));
    }

    [Fact]
    public async Task Ignore_NotOpenOrUnknownGroup_DoesNothing()
    {
        await AddCopiesAsync(2, hash: 7);
        var group = await SingleGroupAsync();
        await bed.Sender.Send(new IgnoreDuplicateGroupCommand(group.Id), Token);

        var again = await bed.Sender.Send(new IgnoreDuplicateGroupCommand(group.Id), Token);
        var unknown = await bed.Sender.Send(new IgnoreDuplicateGroupCommand(Id<DuplicateGroup>.New()), Token);

        Assert.Null(again);
        Assert.Null(unknown);
        Assert.Equal(1, await bed.Library.DuplicateResolutions.CountAsync(Token));
    }

    [Fact]
    public async Task Ignore_NewMemberJoins_GroupOpensAgain()
    {
        await AddCopiesAsync(2, hash: 7);
        var group = await SingleGroupAsync();
        await bed.Sender.Send(new IgnoreDuplicateGroupCommand(group.Id), Token);

        await AddCopiesAsync(1, hash: 7, first: 20);

        Assert.Equal(DuplicateGroupState.Open, (await bed.Library.Duplicates.GetAsync(group.Id, Token))!.State);
    }

    private async Task<IReadOnlyList<MediaItem>> AddCopiesAsync(int count, UInt128 hash, int first = 1)
    {
        var items = Enumerable.Range(first, count).Select(number =>
        {
            var item = bed.LibraryBed.NewItem(number);
            item.SetHashes(new FileHashes { ContentHash = hash });
            return item;
        }).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        await ReconcileAsync();
        return items;
    }

    private async Task ReconcileAsync()
    {
        while ((await bed.Library.Duplicates.ReconcileAsync(DuplicateGroupMaintainer.BatchSize, DateTimeOffset.UtcNow, Token)).Hashes > 0)
        {
        }
    }

    private async Task<DuplicateGroup> SingleGroupAsync() =>
        Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
}
