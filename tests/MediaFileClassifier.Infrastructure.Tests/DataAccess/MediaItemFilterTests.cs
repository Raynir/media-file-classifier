using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for the scoped item reads of <c>MediaItemRepository</c> that the navigation panel uses (E13-S02).
/// </summary>
public sealed class MediaItemFilterTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private IndexedFolder other = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        other = IndexedFolder.Create(bed.Library.Library.Id, @"E:\Other", @"E:\Other", LibraryTestBed.Start);
        await bed.Library.Folders.AddAsync(other, Token);
        await bed.Library.Items.AddRangeAsync(
            [
                Item(bed.Folder, "top.jpg"),
                Item(bed.Folder, "2024/a.jpg"),
                Item(bed.Folder, "2024/trip/b.jpg"),
                Item(bed.Folder, "2024/trip/c.jpg"),
                Item(bed.Folder, "2024_x/d.jpg"),
                Item(bed.Folder, "100%/e.jpg"),
                Item(bed.Folder, "2024/excluded.jpg", MediaItemStatus.Excluded),
                Item(other, "2024/f.jpg"),
                Item(bed.Folder, "old/g.jpg", importedAt: LibraryTestBed.Start.AddDays(-30)),
            ],
            Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Theory]
    [InlineData(null, 8)]
    [InlineData("", 7)]
    [InlineData("2024", 3)]
    [InlineData("2024/trip", 2)]
    [InlineData("2024/trip/", 2)]
    [InlineData("100%", 1)]
    [InlineData("2024_x", 1)]
    [InlineData("missing", 0)]
    public async Task Subfolder_ShowsItsItemsAndTheOnesInside_ButNotExcludedOrLookalikes(string? subfolder, int expected)
    {
        var filter = subfolder is null ? MediaItemFilter.All : new MediaItemFilter { FolderId = bed.Folder.Id, Subfolder = subfolder };

        Assert.Equal(expected, await bed.Library.Items.CountAsync(filter, Token));
        Assert.Equal(expected, (await bed.Library.Items.GetSummariesAsync(filter, 0, 100, Token)).Count);
    }

    [Theory]
    [InlineData("Фото", 2)]
    [InlineData("фото", 2)]
    [InlineData("ФОТО/Ещё", 1)]
    [InlineData("фото/ещё", 1)]
    [InlineData("фот", 0)]
    public async Task Subfolder_IgnoresCaseInEveryAlphabet(string subfolder, int expected)
    {
        await bed.Library.Items.AddRangeAsync([Item(bed.Folder, "Фото/a.jpg"), Item(bed.Folder, "Фото/Ещё/b.jpg"), Item(bed.Folder, "Фотограф/c.jpg")], Token);
        var filter = new MediaItemFilter { FolderId = bed.Folder.Id, Subfolder = subfolder };

        Assert.Equal(expected, await bed.Library.Items.CountAsync(filter, Token));
        Assert.Equal(expected, (await bed.Library.Items.GetSummariesAsync(filter, 0, 100, Token)).Count);
    }

    [Fact]
    public async Task GetSubfoldersAsync_FindsTheChildrenOfAFolderTypedInAnotherCase()
    {
        await bed.Library.Items.AddRangeAsync([Item(bed.Folder, "Фото/Ещё/b.jpg"), Item(bed.Folder, "Фото/Ещё/c.jpg")], Token);

        var subfolders = await bed.Library.Items.GetSubfoldersAsync(bed.Folder.Id, "фото", Token);

        Assert.Equal(["Ещё:2"], subfolders.Select(folder => $"{folder.Name}:{folder.Count}"));
    }

    [Fact]
    public async Task ImportedSince_ShowsOnlyRecentItems()
    {
        var filter = new MediaItemFilter { ImportedSince = LibraryTestBed.Start.AddDays(-7) };

        var page = await bed.Library.Items.GetSummariesAsync(filter, 0, 100, Token);

        Assert.Equal(7, page.Count);
        Assert.DoesNotContain(page, item => item.FileName == "g.jpg");
    }

    [Fact]
    public async Task CountNewerAsync_CountsOnlyItemsTheFilterShows()
    {
        var filter = new MediaItemFilter { FolderId = bed.Folder.Id, Subfolder = "2024" };
        var page = await bed.Library.Items.GetSummariesAsync(filter, 0, 100, Token);

        for (var i = 0; i < page.Count; i++)
        {
            Assert.Equal(i, await bed.Library.Items.CountNewerAsync(filter, page[i].Id, Token));
        }
    }

    [Theory]
    [InlineData("", "100%:1,2024:3,2024_x:1,old:1")]
    [InlineData("2024", "trip:2")]
    [InlineData("2024/trip", "")]
    public async Task GetSubfoldersAsync_ListsTheNextLevelWithCounts(string subfolder, string expected)
    {
        var subfolders = await bed.Library.Items.GetSubfoldersAsync(bed.Folder.Id, subfolder, Token);

        Assert.Equal(expected, string.Join(',', subfolders.Select(folder => $"{folder.Name}:{folder.Count}")));
    }

    private static MediaItem Item(IndexedFolder folder, string path, MediaItemStatus status = MediaItemStatus.Pending, DateTimeOffset? importedAt = null)
    {
        var item = MediaItem.Discover(folder.Id, RelativePath.From(path), MediaType.Image, 1, LibraryTestBed.Start, LibraryTestBed.Start, null, importedAt ?? LibraryTestBed.Start);
        if (status != MediaItemStatus.Discovered)
        {
            item.ChangeStatus(status, LibraryTestBed.Start);
        }

        return item;
    }
}
