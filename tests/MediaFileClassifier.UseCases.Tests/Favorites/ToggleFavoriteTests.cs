using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Favorites;
using MediaFileClassifier.UseCases.Favorites.ToggleFavorite;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Favorites;

/// <summary>
/// Tests for toggling favorites on one or several items (E11-S01-T01).
/// </summary>
public sealed class ToggleFavoriteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly List<ItemsMetadataChanged> published = [];
    private readonly ServiceProvider services;

    public ToggleFavoriteTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .AddSingleton<INotificationHandler<ItemsMetadataChanged>>(new Recorder(published))
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Toggle_OneItem_MarksItAndPublishesTheChange()
    {
        var library = await OpenLibraryAsync(2);
        var item = library.ItemList.Items[0];

        var result = await Sender.Send(new ToggleFavoriteCommand([item.Id]), Token);

        Assert.True(result);
        Assert.True(item.IsFavorite);
        Assert.False(library.ItemList.Items[1].IsFavorite);
        var change = Assert.Single(published);
        Assert.Equal([item.Id], change.ItemIds);
        Assert.True(change.IsFavorite);
    }

    [Fact]
    public async Task Toggle_AFavorite_UnmarksIt()
    {
        var library = await OpenLibraryAsync(1);
        var item = library.ItemList.Items[0];
        item.SetFavorite(true, Now);

        var result = await Sender.Send(new ToggleFavoriteCommand([item.Id]), Token);

        Assert.False(result);
        Assert.False(item.IsFavorite);
        Assert.False(Assert.Single(published).IsFavorite);
    }

    [Fact]
    public async Task Toggle_MixedSelection_MarksAllAndPublishesOnlyTheOnesThatChanged()
    {
        var library = await OpenLibraryAsync(3);
        var items = library.ItemList.Items;
        items[0].SetFavorite(true, Now);

        var result = await Sender.Send(new ToggleFavoriteCommand([.. items.Select(item => item.Id)]), Token);

        Assert.True(result);
        Assert.All(items, item => Assert.True(item.IsFavorite));
        Assert.Equal([items[1].Id, items[2].Id], Assert.Single(published).ItemIds);
        Assert.Equal(1, library.ItemList.FavoriteWrites);
    }

    [Fact]
    public async Task Toggle_AllFavorites_UnmarksAll()
    {
        var library = await OpenLibraryAsync(2);
        var items = library.ItemList.Items;
        items.ForEach(item => item.SetFavorite(true, Now));

        var result = await Sender.Send(new ToggleFavoriteCommand([.. items.Select(item => item.Id)]), Token);

        Assert.False(result);
        Assert.All(items, item => Assert.False(item.IsFavorite));
    }

    [Fact]
    public async Task Toggle_UnknownItemsOrNoItems_DoesNothing()
    {
        var library = await OpenLibraryAsync(1);

        Assert.Null(await Sender.Send(new ToggleFavoriteCommand([Id<MediaItem>.New()]), Token));
        Assert.Null(await Sender.Send(new ToggleFavoriteCommand([]), Token));
        Assert.Empty(published);
        Assert.Equal(0, library.ItemList.FavoriteWrites);
    }

    [Fact]
    public async Task Toggle_WithoutLibrary_DoesNothing()
    {
        Assert.Null(await Sender.Send(new ToggleFavoriteCommand([Id<MediaItem>.New()]), Token));
        Assert.Empty(published);
    }

    private async Task<FakeOpenLibrary> OpenLibraryAsync(int count)
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "fav"), "fav");
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        for (var number = 0; number < count; number++)
        {
            library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From($"IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now));
        }

        await session.SetCurrentAsync(library);
        return library;
    }

    private sealed class Recorder(List<ItemsMetadataChanged> sink) : INotificationHandler<ItemsMetadataChanged>
    {
        public Task Handle(ItemsMetadataChanged notification, CancellationToken cancellationToken)
        {
            sink.Add(notification);
            return Task.CompletedTask;
        }
    }
}
