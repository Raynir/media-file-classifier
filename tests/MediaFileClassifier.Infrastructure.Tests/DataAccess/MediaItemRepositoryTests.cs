using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
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
        folder.ChangeRules(new FolderRules { IncludeSubfolders = false, ExcludePatterns = ["**/.thumbnails/**"], IncludeHidden = true, IncludeCloudPlaceholders = true });
        folder.Relink(@"E:\Photos", @"E:\Photos", bed.Clock.Tick());
        folder.MarkOffline(bed.Clock.Tick());

        await bed.Library.Folders.UpdateAsync(folder, Token);

        var loaded = Assert.Single(await bed.Library.Folders.GetAllAsync(Token));
        Assert.Equivalent(folder, loaded);
    }

    [Fact]
    public async Task GetFileStatesAsync_ReturnsTheFolderItemsFileFacts()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
        var otherFolder = IndexedFolder.Create(bed.Library.Library.Id, @"E:\Other", @"E:\Other", bed.Clock.Tick());
        await bed.Library.Folders.AddAsync(otherFolder, Token);
        await bed.Library.Items.AddAsync(
            MediaItem.Discover(otherFolder.Id, item.Path, MediaType.Image, 1, bed.Clock.Tick(), bed.Clock.Tick(), null, bed.Clock.Tick()),
            Token);

        var states = await bed.Library.Items.GetFileStatesAsync(bed.Folder.Id, Token);

        Assert.Equal([new MediaItemFileState(item.Path, item.SizeBytes, item.FileModifiedAt)], states);
    }

    [Fact]
    public async Task GetByStatusAsync_FiltersByStatusAndFolder()
    {
        var other = IndexedFolder.Create(bed.Library.Library.Id, @"E:\Other", @"E:\Other", LibraryTestBed.Start);
        await bed.Library.Folders.AddAsync(other, Token);
        var discovered = bed.NewItem(1);
        var pending = bed.NewItem(2);
        pending.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
        var elsewhere = MediaItem.Discover(other.Id, RelativePath.From("a.jpg"), MediaType.Image, 1, LibraryTestBed.Start, LibraryTestBed.Start, null, LibraryTestBed.Start);
        await bed.Library.Items.AddRangeAsync([discovered, pending, elsewhere], Token);

        var inFolder = await bed.Library.Items.GetByStatusAsync(MediaItemStatus.Discovered, bed.Folder.Id, Token);
        var everywhere = await bed.Library.Items.GetByStatusAsync(MediaItemStatus.Discovered, cancellationToken: Token);

        Assert.Equal([discovered.Id], inFolder.Select(item => item.Id));
        Assert.Equal(new[] { discovered.Id, elsewhere.Id }.Select(id => id.ToString()).Order(StringComparer.Ordinal), everywhere.Select(item => item.Id.ToString()));
    }

    [Fact]
    public async Task GetExactDuplicatesAsync_GroupsItemsThatShareAContentHash()
    {
        var items = Enumerable.Range(1, 5).Select(bed.NewItem).ToList();
        UInt128?[] hashes = [10, 10, 20, 10, null];
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SetHashes(items[i].Hashes with { ContentHash = hashes[i] });
        }

        await bed.Library.Items.AddRangeAsync(items, Token);

        var group = Assert.Single(await bed.Library.Items.GetExactDuplicatesAsync(Token));

        Assert.Equal((UInt128)10, group.ContentHash);
        Assert.Equal(new[] { items[0].Id, items[1].Id, items[3].Id }.Select(id => id.ToString()).Order(StringComparer.Ordinal), group.ItemIds.Select(id => id.ToString()));
    }

    [Fact]
    public async Task GetExactDuplicatesAsync_NoSharedHash_ReturnsNothing()
    {
        await bed.Library.Items.AddAsync(bed.NewItem(1), Token);

        Assert.Empty(await bed.Library.Items.GetExactDuplicatesAsync(Token));
    }

    [Fact]
    public async Task UpdateAsync_WithMetadata_StoresBothAndReplacesOlderMetadata()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
        Assert.Null(await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token));

        item.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(item, new TechnicalMetadata { ItemId = item.Id, Width = 640, ExtractedAt = LibraryTestBed.Start }, Token);
        await bed.Library.Items.UpdateAsync(
            item,
            new TechnicalMetadata { ItemId = item.Id, Width = 800, Warnings = [new ExtractionWarning("ThumbnailFailed", "bad")], ExtractedAt = LibraryTestBed.Start },
            Token);

        Assert.Equal(MediaItemStatus.Pending, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
        var stored = await bed.Library.Items.GetTechnicalMetadataAsync(item.Id, Token);
        Assert.Equal(800, stored!.Width);
        Assert.Equal([new ExtractionWarning("ThumbnailFailed", "bad")], stored.Warnings);
        await Assert.ThrowsAsync<ArgumentException>(() => bed.Library.Items.UpdateAsync(item, new TechnicalMetadata { ItemId = Id<MediaItem>.New(), ExtractedAt = LibraryTestBed.Start }, Token));
    }

    [Fact]
    public async Task UpdateAsync_FailedItem_StoresTheErrorUntilTheStatusChanges()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        item.MarkFailed(new MediaItemError("import.media", nameof(IOException), IsPermanent: false), "Locked", LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(item, Token);
        var failed = await bed.Library.Items.GetAsync(item.Id, Token);
        var storedError = failed!.Error;
        failed.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(failed, Token);

        Assert.Equal(new MediaItemError("import.media", nameof(IOException), false), storedError);
        Assert.Equal("Locked", item.StatusReason);
        Assert.Null((await bed.Library.Items.GetAsync(item.Id, Token))!.Error);
    }

    [Fact]
    public void FolderRules_StoredBeforeHiddenSetting_ReadAsDefaults()
    {
        var converter = new FolderRulesConverter();

        var rules = (FolderRules)converter.ConvertFromProvider("""{"includeSubfolders":true,"includePatterns":[],"excludePatterns":["raw"]}""")!;

        Assert.False(rules.IncludeHidden);
        Assert.False(rules.IncludeCloudPlaceholders);
        Assert.Equal(["raw"], rules.ExcludePatterns);
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
