using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Import.ImportFiles;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.IndexWholeFolder;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// Making a partial folder a full folder (E03-S05-T03, MFC-656), with Index whole folder or Add folder, end to end.
/// </summary>
public sealed class IndexWholeFolderTests : IAsyncLifetime
{
    private readonly TemporaryFolder downloads = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        downloads.Dispose();
    }

    [Fact]
    public async Task IndexWholeFolder_ImportsTheRest_AndKeepsTheItemsAndTheirMetadata()
    {
        var (partial, picked) = await ImportPickedAsync();
        Copy(SampleMedia.StaticGif, "c.gif");
        Copy(SampleMedia.WebP, "2024/d.webp");

        Assert.True(await bed.Sender.Send(new IndexWholeFolderCommand(partial.Id), Token));
        await bed.WaitUntilIdleAsync();

        Assert.False((await bed.Library.Folders.GetAsync(partial.Id, Token))!.SelectedFilesOnly);
        await AssertMetadataKeptAsync(partial, picked);
        Assert.Equal(["2024/d.webp", "a.png", "b.jpg", "c.gif"], await PathsAsync(partial.Id));
        Assert.All(await bed.Library.Items.GetByFolderAsync(partial.Id, Token), item => Assert.Equal(MediaItemStatus.Pending, item.Status));
    }

    [Fact]
    public async Task IndexWholeFolder_OnAFullFolder_DoesNothing() =>
        Assert.False(await bed.Sender.Send(new IndexWholeFolderCommand(bed.Folder.Id), Token));

    [Fact]
    public async Task AddFolder_ThePartialFolderItself_IndexesItWhole()
    {
        var (partial, picked) = await ImportPickedAsync();
        Copy(SampleMedia.StaticGif, "c.gif");

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([LocalFolder.ToHandle(downloads.Path)]), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Empty(result.Added);
        Assert.Empty(result.Refused);
        Assert.Equal(partial.Id, Assert.Single(result.IndexedWhole).Id);
        await AssertMetadataKeptAsync(partial, picked);
        Assert.Equal(["a.png", "b.jpg", "c.gif"], await PathsAsync(partial.Id));
    }

    [Fact]
    public async Task AddFolder_InsideAPartialFolder_TakesItsItemsThere()
    {
        var picked = await ImportAsync(Copy(SampleMedia.PngWithAlpha, "a.png"), Copy(SampleMedia.JpegWithExif, "2024/b.jpg"));
        var partial = Assert.Single(picked.AddedFolders);
        var b = (await bed.Library.Items.GetByFolderAsync(partial.Id, Token)).Single(item => item.Path.Value == "2024/b.jpg");
        await bed.Library.Metadata.AddTagAsync([b.Id], TagName.From("beach"), Token);
        Copy(SampleMedia.StaticGif, "2024/c.gif");

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([LocalFolder.ToHandle(Path.Combine(downloads.Path, "2024"))]), Token);
        await bed.WaitUntilIdleAsync();

        var added = Assert.Single(result.Added);
        Assert.Empty(result.Refused);
        Assert.Equal(["a.png"], await PathsAsync(partial.Id));
        Assert.Equal(["b.jpg", "c.gif"], await PathsAsync(added.Id));
        var moved = (await bed.Library.Items.GetAsync(b.Id, Token))!;
        Assert.Equal(added.Id, moved.FolderId);
        Assert.Contains((await bed.Library.Metadata.GetAsync(b.Id, Token)).Tags, tag => tag.Tag == TagName.From("beach"));
    }

    [Fact]
    public async Task AddFolder_InsideAPartialFolder_RemovesItWhenNothingIsLeft()
    {
        // A partial folder whose only file is in a subfolder (picked files there join it).
        var handle = LocalFolder.ToHandle(downloads.Path);
        var partial = IndexedFolder.CreateForSelectedFiles(bed.Library.Library.Id, downloads.Path, handle.Id, DataAccess.LibraryTestBed.Start);
        await bed.Library.Folders.AddAsync(partial, Token);
        var picked = await ImportAsync(Copy(SampleMedia.JpegWithExif, "2024/b.jpg"));
        Assert.Empty(picked.AddedFolders);
        var b = Assert.Single(await bed.Library.Items.GetByFolderAsync(partial.Id, Token));

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([LocalFolder.ToHandle(Path.Combine(downloads.Path, "2024"))]), Token);
        await bed.WaitUntilIdleAsync();

        var added = Assert.Single(result.Added);
        Assert.Null(await bed.Library.Folders.GetAsync(partial.Id, Token));
        Assert.Equal(added.Id, (await bed.Library.Items.GetAsync(b.Id, Token))!.FolderId);
        Assert.Equal(["b.jpg"], await PathsAsync(added.Id));
    }

    [Fact]
    public async Task IndexWholeFolder_WithAFolderInside_MergesIt_SoNoFileIsListedTwice()
    {
        var (partial, _) = await ImportPickedAsync();
        Copy(SampleMedia.StaticGif, "2024/c.gif");
        var inner = Assert.Single((await bed.Sender.Send(new AddLibraryFoldersCommand([LocalFolder.ToHandle(Path.Combine(downloads.Path, "2024"))]), Token)).Added);
        await bed.WaitUntilIdleAsync();
        var c = Assert.Single(await bed.Library.Items.GetByFolderAsync(inner.Id, Token));

        await bed.Sender.Send(new IndexWholeFolderCommand(partial.Id), Token);
        await bed.WaitUntilIdleAsync();

        Assert.Null(await bed.Library.Folders.GetAsync(inner.Id, Token));
        Assert.Equal(["2024/c.gif", "a.png", "b.jpg"], await PathsAsync(partial.Id));
        Assert.Equal(partial.Id, (await bed.Library.Items.GetAsync(c.Id, Token))!.FolderId);
    }

    [Fact]
    public async Task AddFolder_InsideNestedPartialFolders_SplitsEach()
    {
        var inner = Assert.Single((await ImportAsync(Copy(SampleMedia.JpegWithExif, "2024/b.jpg"))).AddedFolders);
        var outer = Assert.Single((await ImportAsync(Copy(SampleMedia.PngWithAlpha, "a.png"))).AddedFolders);
        Assert.Empty((await ImportAsync(Copy(SampleMedia.StaticGif, "2024/sub/c.gif"))).AddedFolders);

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([LocalFolder.ToHandle(Path.Combine(downloads.Path, "2024", "sub"))]), Token);
        await bed.WaitUntilIdleAsync();

        var added = Assert.Single(result.Added);
        Assert.Equal(["c.gif"], await PathsAsync(added.Id));
        Assert.Equal(["b.jpg"], await PathsAsync(inner.Id));
        Assert.Equal(["a.png"], await PathsAsync(outer.Id));
    }

    private async Task<(IndexedFolder Partial, MediaItem Picked)> ImportPickedAsync()
    {
        var result = await ImportAsync(Copy(SampleMedia.PngWithAlpha, "a.png"), Copy(SampleMedia.JpegWithExif, "b.jpg"));
        await bed.WaitUntilIdleAsync();
        var partial = Assert.Single(result.AddedFolders);
        var picked = (await bed.Library.Items.GetByFolderAsync(partial.Id, Token)).Single(item => item.Path.Value == "a.png");
        picked.SetFavorite(true, DataAccess.LibraryTestBed.Start);
        await bed.Library.Items.UpdateAsync(picked, Token);
        await bed.Library.Metadata.AddTagAsync([picked.Id], TagName.From("beach"), Token);
        await bed.Library.Metadata.UpdateAsync(picked.Id, metadata => metadata.EditValue(DescriptiveField.Notes, FieldValue.FromText("Sunset"), DataAccess.LibraryTestBed.Start), Token);
        return (partial, picked);
    }

    private async Task AssertMetadataKeptAsync(IndexedFolder partial, MediaItem picked)
    {
        var after = (await bed.Library.Items.GetAsync(picked.Id, Token))!;
        Assert.Equal(partial.Id, after.FolderId);
        Assert.Equal("a.png", after.Path.Value);
        Assert.True(after.IsFavorite);
        var metadata = await bed.Library.Metadata.GetAsync(picked.Id, Token);
        Assert.Contains(metadata.Tags, tag => tag.Tag == TagName.From("beach"));
        Assert.Equal("Sunset", metadata.GetUserValue(DescriptiveField.Notes)!.Value.Text);
        Assert.Single(await bed.Library.Items.GetByFolderAsync(partial.Id, Token), item => item.Path.Value == "a.png");
    }

    private string Copy(string source, string relativePath)
    {
        var target = Path.Combine(downloads.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private async Task<ImportFilesResult> ImportAsync(params string[] paths)
    {
        var result = await bed.Sender.Send(new ImportFilesCommand([.. paths.Select(LocalFolder.ToLocation)]), Token);
        await bed.WaitUntilIdleAsync();
        return result;
    }

    private async Task<IReadOnlyList<string>> PathsAsync(Id<IndexedFolder> folderId) =>
        [.. (await bed.Library.Items.GetByFolderAsync(folderId, Token)).Select(item => item.Path.Value).Order(StringComparer.Ordinal)];
}
