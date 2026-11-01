using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="MediaItemRepository"/>, <see cref="FolderRepository"/> and
/// <see cref="ModelRegistrationRepository"/> against a real library database (E01-S03-T06).
/// </summary>
public sealed class MediaItemRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task AddAsync_ThenGetAsync_ReturnsEqualItem()
    {
        var item = bed.NewItem(7);

        await bed.Library.Items.AddAsync(item, Token);
        var loaded = await bed.Library.Items.GetAsync(item.Id, Token);

        Assert.NotNull(loaded);
        Assert.Equivalent(item, loaded);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await bed.Library.Items.GetAsync(Id<MediaItem>.New(), Token));
    }

    [Fact]
    public async Task FindByPathAsync_FindsItemInFolder()
    {
        var item = bed.NewItem(3);
        await bed.Library.Items.AddAsync(item, Token);

        var found = await bed.Library.Items.FindByPathAsync(bed.Folder.Id, RelativePath.From("album/IMG_00003.jpg"), Token);

        Assert.Equal(item.Id, found?.Id);
        Assert.Null(await bed.Library.Items.FindByPathAsync(bed.Folder.Id, RelativePath.From("album/other.jpg"), Token));
    }

    [Fact]
    public async Task AddAsync_SamePathTwice_Fails()
    {
        await bed.Library.Items.AddAsync(bed.NewItem(1), Token);

        await Assert.ThrowsAsync<DbUpdateException>(() => bed.Library.Items.AddAsync(bed.NewItem(1), Token));
    }

    [Fact]
    public async Task AddRangeAsync_StoresEveryItemAcrossBatches()
    {
        var items = Enumerable.Range(0, (MediaItemRepository.BatchSize * 2) + 37).Select(bed.NewItem).ToList();

        await bed.Library.Items.AddRangeAsync(items, Token);

        Assert.Equal(items.Count, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task GetPageAsync_PagesInIdOrder()
    {
        var items = Enumerable.Range(0, 30).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);

        var page = await bed.Library.Items.GetPageAsync(10, 5, Token);

        var expected = items.Select(item => item.Id.ToString()).Order(StringComparer.Ordinal).Skip(10).Take(5);
        Assert.Equal(expected, page.Select(item => item.Id.ToString()));
    }

    [Fact]
    public async Task UpdateAsync_StoresChanges()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        item.ChangeStatus(MediaItemStatus.Pending, bed.Clock.Tick());
        item.SetFavorite(true, bed.Clock.Tick());
        item.MarkReviewed(bed.Clock.Tick());
        item.SetHashes(new FileHashes { Fingerprint = 1, ContentHash = 2, PerceptualHash = 3 });
        item.Relocate(bed.Folder.Id, RelativePath.From("moved/IMG_00001.jpg"));
        await bed.Library.Items.UpdateAsync(item, Token);

        Assert.Equivalent(item, await bed.Library.Items.GetAsync(item.Id, Token));
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        Assert.True(await bed.Library.Items.DeleteAsync(item.Id, Token));
        Assert.False(await bed.Library.Items.DeleteAsync(item.Id, Token));
        Assert.Null(await bed.Library.Items.GetAsync(item.Id, Token));
    }

    [Fact]
    public async Task Folders_UpdateAsync_StoresRulesAndRelink()
    {
        var folder = await bed.Library.Folders.GetAsync(bed.Folder.Id, Token);
        Assert.NotNull(folder);
        folder.ChangeRules(new FolderRules { IncludeSubfolders = false, ExcludePatterns = ["**/.thumbnails/**"] });
        folder.Relink(@"E:\Photos", @"E:\Photos", bed.Clock.Tick());
        folder.MarkOffline(bed.Clock.Tick());

        await bed.Library.Folders.UpdateAsync(folder, Token);

        var loaded = Assert.Single(await bed.Library.Folders.GetAllAsync(Token));
        Assert.Equivalent(folder, loaded);
    }

    [Fact]
    public async Task Models_GetAllAsync_ReturnsRegistrationsInOrder()
    {
        var second = ModelRegistration.Create("local", "qwen-vl", "2.5", "abc", null, bed.Clock.Tick());
        await bed.Library.Models.AddAsync(second, Token);

        var models = await bed.Library.Models.GetAllAsync(Token);

        Assert.Equal([bed.Model.Id, second.Id], models.Select(model => model.Id));
    }
}
