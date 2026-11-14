using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Browsing.ThumbnailTileSize;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Browsing;

/// <summary>
/// Tests for the media browser's use cases (E14-S01): the library result source, the remembered tile size and the
/// items' locations.
/// </summary>
public sealed class BrowsingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public BrowsingTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();

        // These cover the M1 item list (D177), which browsing uses in newest-import order (E10-S04).
        services.GetRequiredService<SearchResultSource>().SetSort(new SortChoice(SortMode.ImportDate, Descending: true));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task ResultSource_WithoutLibrary_IsEmpty()
    {
        var source = services.GetRequiredService<IMediaResultSource>();

        Assert.Equal(0, await source.CountAsync(Token));
        Assert.Empty(await source.GetPageAsync(0, 10, Token));
    }

    [Fact]
    public async Task ResultSource_PagesTheOpenLibraryNewestFirst()
    {
        var library = await OpenLibraryWithItemsAsync(5);
        var source = services.GetRequiredService<IMediaResultSource>();

        var page = await source.GetPageAsync(1, 2, Token);

        Assert.Equal(5, await source.CountAsync(Token));
        Assert.Equal([library.ItemList.Items[3].Id, library.ItemList.Items[2].Id], page.Select(item => item.Id));
        Assert.Equal(1, await source.PositionOfAsync(library.ItemList.Items[3].Id, Token));
    }

    [Fact]
    public async Task ResultSource_ReportsOpeningClosingAndItemChanges()
    {
        var source = services.GetRequiredService<IMediaResultSource>();
        var changes = 0;
        source.Changed += (_, _) => changes++;

        var library = await OpenLibraryWithItemsAsync(1);
        library.ItemList.RaiseChanged();
        await session.SetCurrentAsync(null);
        library.ItemList.RaiseChanged();

        // Open, item change, close; the closed library's changes are no longer watched.
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task TileSize_IsRememberedInTheOpenLibrary()
    {
        Assert.Null(await Sender.Send(new GetThumbnailTileSizeQuery(), Token));
        var library = await OpenLibraryWithItemsAsync(0);
        Assert.Equal(LibrarySettings.DefaultThumbnailTileSize, await Sender.Send(new GetThumbnailTileSizeQuery(), Token));

        await Sender.Send(new SetThumbnailTileSizeCommand(224), Token);
        await Sender.Send(new SetThumbnailTileSizeCommand(224), Token);

        Assert.Equal(224, await Sender.Send(new GetThumbnailTileSizeQuery(), Token));
        Assert.Equal(1, library.LibraryUpdates);
    }

    [Fact]
    public async Task TileSize_WithoutLibrary_IsNotSaved()
    {
        await Sender.Send(new SetThumbnailTileSizeCommand(224), Token);

        Assert.Null(await Sender.Send(new GetThumbnailTileSizeQuery(), Token));
    }

    [Fact]
    public async Task ItemLocations_GiveThePathAndAvailability_InTheOrderAsked()
    {
        var library = await OpenLibraryWithItemsAsync(3);
        var items = library.ItemList.Items;
        items[1].ChangeStatus(MediaItemStatus.Missing, Now);
        var drive = IndexedFolder.Create(library.Library.Id, @"E:\Trip", @"E:\Trip", Now);
        drive.MarkOffline(Now);
        library.FolderList.Folders.Add(drive);
        var away = MediaItem.Discover(drive.Id, RelativePath.From("beach.jpg"), MediaType.Image, 100, Now, Now, null, Now);
        library.ItemList.Items.Add(away);

        var locations = await Sender.Send(
            new GetItemLocationsQuery([away.Id, items[1].Id, Domain.Identity.Id<MediaItem>.New(), items[0].Id]), Token);

        Assert.Equal([away.Id, items[1].Id, items[0].Id], locations.Select(location => location.ItemId));
        Assert.Equal(
            [ItemAvailability.Offline, ItemAvailability.Missing, ItemAvailability.Available],
            locations.Select(location => location.Availability));
        Assert.Equal(Path.Combine(@"D:\Photos", "album", "IMG_0.jpg"), locations[2].FullPath);
        Assert.Equal((@"E:\Trip", "beach.jpg", @"E:\Trip"), (locations[0].Location.Folder.Id, locations[0].Location.RelativePath, locations[0].FolderName));
    }

    private async Task<FakeOpenLibrary> OpenLibraryWithItemsAsync(int count)
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "browse"), "browse");
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        for (var number = 0; number < count; number++)
        {
            library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From($"album/IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now));
        }

        await session.SetCurrentAsync(library);
        return library;
    }
}
