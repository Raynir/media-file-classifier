using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Browsing;

/// <summary>
/// Tests for the selection value and its resolution into the items bulk operations work on (E14-S03-T01).
/// </summary>
public sealed class ItemSelectionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public ItemSelectionTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Resolve_ExplicitIds_LeavesOutItemsNoLongerInTheLibrary()
    {
        var items = await OpenLibraryWithItemsAsync(5);
        var gone = Id<MediaItem>.New();

        var ids = await Sender.Send(new ResolveItemSelectionQuery(ItemSelection.Of([items[1].Id, items[3].Id, gone])), Token);

        Assert.Equal(new[] { items[1].Id, items[3].Id }.ToHashSet(), ids.ToHashSet());
        Assert.Empty(await Sender.Send(new ResolveItemSelectionQuery(ItemSelection.None), Token));
    }

    [Fact]
    public async Task Resolve_All_CoversResultsUpToTheNewest_ExceptDeselected_PlusNewerSelected()
    {
        // Newest last, as the library adds them; Select all ran when items[5] was the newest.
        var items = await OpenLibraryWithItemsAsync(8);
        var selection = ItemSelection.All(items[5].Id, [items[2].Id, items[7].Id], count: 6);

        var ids = await Sender.Send(new ResolveItemSelectionQuery(selection), Token);

        Assert.Equal(new[] { items[0], items[1], items[3], items[4], items[5], items[7] }.Select(item => item.Id).ToHashSet(), ids.ToHashSet());
        Assert.Equal(6, ids.Count);
    }

    [Fact]
    public void Contains_FollowsTheModeAndExceptions()
    {
        var older = new Id<MediaItem>(Guid.CreateVersion7(Now));
        var boundary = new Id<MediaItem>(Guid.CreateVersion7(Now.AddSeconds(1)));
        var newer = new Id<MediaItem>(Guid.CreateVersion7(Now.AddSeconds(2)));

        var all = ItemSelection.All(boundary, [older], count: 9);
        var explicitIds = ItemSelection.Of([older]);

        Assert.Equal((false, true, false), (all.Contains(older), all.Contains(boundary), all.Contains(newer)));
        Assert.Equal((true, false), (explicitIds.Contains(older), explicitIds.Contains(newer)));
        Assert.Equal((SelectionKind.Multiple, SelectionKind.Single, SelectionKind.None), (all.Kind, explicitIds.Kind, ItemSelection.None.Kind));
    }

    [Fact]
    public void IsUpTo_MatchesTheLibrarysTextOrderOfIds()
    {
        // The library compares IDs as lower-case text; the selection compares Guids. They must agree.
        var random = new Random(7);
        for (var pass = 0; pass < 2000; pass++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var a = new Id<MediaItem>(pass % 2 == 0 ? new Guid(bytes) : Guid.CreateVersion7());
            random.NextBytes(bytes);
            var b = new Id<MediaItem>(new Guid(bytes));

            Assert.Equal(string.CompareOrdinal(a.ToString(), b.ToString()) <= 0, ItemSelection.IsUpTo(a, b));
        }
    }

    private async Task<List<MediaItem>> OpenLibraryWithItemsAsync(int count)
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "select"), "select");
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        var items = Enumerable.Range(0, count)
            .Select(number => MediaItem.Discover(folder.Id, RelativePath.From($"album/IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now))
            .OrderBy(item => item.Id.Value)
            .ToList();

        // IDs made in one millisecond sort at random, so keep them in ID order, newest last, as the library does.
        library.ItemList.Items.AddRange(items);
        await session.SetCurrentAsync(library);
        return items;
    }
}
