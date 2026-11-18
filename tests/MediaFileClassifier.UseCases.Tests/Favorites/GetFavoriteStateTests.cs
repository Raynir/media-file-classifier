using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Favorites.GetFavoriteState;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Favorites;

/// <summary>
/// Tests for reading the favorite state of a selection (E11-S01-T02).
/// </summary>
public sealed class GetFavoriteStateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public GetFavoriteStateTests()
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
    public async Task State_IsTrueOnlyWhenEveryItemIsAFavorite()
    {
        var items = await OpenLibraryAsync(3);
        items[0].SetFavorite(true, Now);
        items[1].SetFavorite(true, Now);
        var ids = items.Select(item => item.Id).ToList();

        Assert.True(await Sender.Send(new GetFavoriteStateQuery(ids.Take(2).ToList()), Token));
        Assert.False(await Sender.Send(new GetFavoriteStateQuery(ids), Token));
    }

    [Fact]
    public async Task State_WithoutItemsOrLibrary_IsFalse()
    {
        Assert.False(await Sender.Send(new GetFavoriteStateQuery([Id<MediaItem>.New()]), Token));

        await OpenLibraryAsync(1);

        Assert.False(await Sender.Send(new GetFavoriteStateQuery([]), Token));
        Assert.False(await Sender.Send(new GetFavoriteStateQuery([Id<MediaItem>.New()]), Token));
    }

    private async Task<List<MediaItem>> OpenLibraryAsync(int count)
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "fav"), "fav");
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        for (var number = 0; number < count; number++)
        {
            library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From($"IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now));
        }

        await session.SetCurrentAsync(library);
        return library.ItemList.Items;
    }
}
