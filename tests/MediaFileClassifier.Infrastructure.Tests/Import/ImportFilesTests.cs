using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;
using MediaFileClassifier.UseCases.Import.ImportFiles;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// Importing selected files (E03-S05-T02, MFC-654): end to end, with a real library, real files and the job system.
/// </summary>
public sealed class ImportFilesTests : IAsyncLifetime
{
    private readonly TemporaryFolder downloads = new();
    private readonly TemporaryFolder desktop = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        downloads.Dispose();
        desktop.Dispose();
    }

    [Fact]
    public async Task Import_FilesFromThreeFolders_ImportsEachIntoItsFolder()
    {
        var indexed = bed.Copy(SampleMedia.JpegWithExif, "x.jpg");
        var a = Copy(downloads, SampleMedia.PngWithAlpha, "a.png");
        Copy(downloads, SampleMedia.WebP, "not-picked.webp");
        var b = Copy(desktop, SampleMedia.StaticGif, "b.gif");
        var c = Copy(desktop, SampleMedia.NonAsciiName, "фото.jpg");

        var result = await ImportAsync(indexed, a, b, c);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(4, result.Imported.Count);
        Assert.Equal(2, result.AddedFolders.Count);
        Assert.All(result.AddedFolders, folder => Assert.True(folder.SelectedFilesOnly));
        Assert.Equal(["x.jpg"], await PathsAsync(bed.Folder));
        Assert.Equal(["a.png"], await PathsAsync(FolderAt(result, downloads)));
        Assert.Equal(["b.gif", "фото.jpg"], await PathsAsync(FolderAt(result, desktop)));
        var all = await bed.Library.Items.GetPageAsync(0, 100, Token);
        Assert.Equal(4, all.Count);
        Assert.All(all, item =>
        {
            Assert.Equal(MediaItemStatus.Pending, item.Status);
            Assert.NotNull(item.Hashes.ContentHash);
        });
    }

    [Fact]
    public async Task Import_NewPartialFolder_GetsItsVolumeId_RightAway()
    {
        var volumes = new FakeVolumeLocator();
        volumes.Volumes[Path.GetPathRoot(downloads.Path)!] = "1A2B-3C4D";
        await using var own = await ImportTestBed.CreateAsync(configure: services => services.AddSingleton<IVolumeLocator>(volumes));
        var file = Copy(downloads, SampleMedia.PngWithAlpha, "a.png");

        var result = await own.Sender.Send(new ImportFilesCommand([LocalFolder.ToLocation(file)]), Token);
        await own.WaitUntilIdleAsync();

        Assert.Equal("1A2B-3C4D", Assert.Single(result.AddedFolders).VolumeId);
        Assert.Equal("1A2B-3C4D", (await own.Library.Folders.GetAsync(result.AddedFolders[0].Id, Token))!.VolumeId);
    }

    [Fact]
    public async Task Import_FileInSubfolderOfPartialFolder_JoinsIt()
    {
        var first = await ImportAsync(Copy(downloads, SampleMedia.PngWithAlpha, "a.png"));
        var second = await ImportAsync(Copy(downloads, SampleMedia.StaticGif, "2024/b.gif"));
        await bed.WaitUntilIdleAsync();

        Assert.Empty(second.AddedFolders);
        Assert.Equal(["2024/b.gif", "a.png"], await PathsAsync(Assert.Single(first.AddedFolders)));
    }

    [Fact]
    public async Task Import_SameFilesAgain_AreAlreadyInTheLibrary()
    {
        var a = Copy(downloads, SampleMedia.PngWithAlpha, "a.png");
        var x = bed.Copy(SampleMedia.JpegWithExif, "x.jpg");
        await ImportAsync(a, x);
        await bed.WaitUntilIdleAsync();

        var result = await ImportAsync(a, x, a);

        Assert.Empty(result.Imported);
        Assert.Empty(result.AddedFolders);
        Assert.Equal(2, result.AlreadyInLibrary.Count);
    }

    [Fact]
    public async Task Import_UnsupportedUnreadableAndRuleExcludedFiles_AreReported()
    {
        bed.Folder.ChangeRules(bed.Folder.Rules with { ExcludePatterns = ["/raw"] });
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        var excluded = bed.Copy(SampleMedia.JpegWithExif, "raw/r.jpg");
        var text = Path.Combine(downloads.Path, "notes.txt");
        await File.WriteAllTextAsync(text, "not media", Token);
        var fake = Path.Combine(downloads.Path, "fake.jpg");
        await File.WriteAllTextAsync(fake, "not a jpeg", Token);
        var gone = Path.Combine(downloads.Path, "gone.jpg");

        var result = await ImportAsync(excluded, text, fake, gone);

        Assert.Equal([LocalFolder.ToLocation(excluded)], result.ExcludedByRules);
        Assert.Equal([LocalFolder.ToLocation(text), LocalFolder.ToLocation(fake)], result.Unsupported);
        Assert.Equal([LocalFolder.ToLocation(gone)], result.Unreadable);
        Assert.Empty(result.Imported);
        Assert.Empty(await bed.Library.Items.GetPageAsync(0, 100, Token));
    }

    [Fact]
    public async Task Import_FileTheUserRemoved_IsRestored()
    {
        var a = Copy(downloads, SampleMedia.PngWithAlpha, "a.png");
        await ImportAsync(a);
        await bed.WaitUntilIdleAsync();
        var item = Assert.Single(await bed.Library.Items.GetPageAsync(0, 100, Token));
        await bed.Sender.Send(new ExcludeItemsCommand([item.Id]), Token);
        await bed.WaitUntilIdleAsync();

        var result = await ImportAsync(a);
        await bed.WaitUntilIdleAsync();

        Assert.Equal([LocalFolder.ToLocation(a)], result.Imported);
        Assert.Equal(MediaItemStatus.Pending, (await bed.Library.Items.GetAsync(item.Id, Token))!.Status);
    }

    private static string Copy(TemporaryFolder folder, string source, string relativePath)
    {
        var target = Path.Combine(folder.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static IndexedFolder FolderAt(ImportFilesResult result, TemporaryFolder folder) =>
        result.AddedFolders.Single(added => string.Equals(added.PlatformHandle, LocalFolder.ToHandle(folder.Path).Id, StringComparison.OrdinalIgnoreCase));

    private async Task<ImportFilesResult> ImportAsync(params string[] paths) =>
        await bed.Sender.Send(new ImportFilesCommand([.. paths.Select(LocalFolder.ToLocation)]), Token);

    private async Task<IReadOnlyList<string>> PathsAsync(IndexedFolder folder) =>
        [.. (await bed.Library.Items.GetByFolderAsync(folder.Id, Token)).Select(item => item.Path.Value).Order(StringComparer.Ordinal)];
}
