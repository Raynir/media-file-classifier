using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediaFileClassifier.UseCases.Rescan;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// A partial folder (D012, MFC-651) is checked only for the files it tracks, by its import and by a rescan.
/// </summary>
public sealed class PartialFolderRescanTests : IAsyncLifetime
{
    private readonly TemporaryFolder downloads = new();
    private ImportTestBed bed = null!;
    private IndexedFolder partial = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        partial = IndexedFolder.CreateForSelectedFiles(bed.Library.Library.Id, downloads.Path, downloads.Path, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(partial, Token);
        Copy(SampleMedia.JpegWithExif, "picked.jpg");
        Copy(SampleMedia.PngWithAlpha, "sub/picked.png");
        Copy(SampleMedia.WebP, "other.webp");
        Copy(SampleMedia.StaticGif, "sub/other.gif");
        await TrackAsync("picked.jpg", MediaType.Image);
        await TrackAsync("sub/picked.png", MediaType.Image);
        await bed.Sender.Send(new ImportFoldersCommand([partial.Id]), Token);
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        downloads.Dispose();
    }

    [Fact]
    public async Task Import_PartialFolder_ProcessesOnlyTrackedFiles()
    {
        var items = await bed.Library.Items.GetByFolderAsync(partial.Id, Token);

        Assert.Equal(["picked.jpg", "sub/picked.png"], items.Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        Assert.All(items, item => Assert.NotEqual(MediaItemStatus.Discovered, item.Status));
        Assert.All(items, item => Assert.NotNull(item.Hashes.ContentHash));
    }

    [Fact]
    public async Task Reconcile_PartialFolder_IgnoresFilesThatWerentSelected()
    {
        Copy(SampleMedia.AnimatedGif, "new.gif");
        File.AppendAllBytes(Path.Combine(downloads.Path, "picked.jpg"), [1, 2, 3]);
        File.Delete(Path.Combine(downloads.Path, "sub/picked.png"));

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [partial], Token);

        Assert.Empty(result.New);
        Assert.Equal("picked.jpg", Assert.Single(result.Modified).Item.Path.Value);
        Assert.Equal("sub/picked.png", Assert.Single(result.Missing).Path.Value);
        Assert.Empty(result.Offline);
    }

    [Fact]
    public async Task Rescan_PartialFolder_LeavesUntrackedFilesOut()
    {
        Copy(SampleMedia.AnimatedGif, "new.gif");

        await ReconcilerTests.RescanAsync(bed, partial);
        await bed.WaitUntilIdleAsync();

        Assert.Equal(2, (await bed.Library.Items.GetByFolderAsync(partial.Id, Token)).Count);
    }

    [Fact]
    public async Task Reconcile_PartialFolderGone_IsOfflineAndNothingIsMissing()
    {
        Directory.Delete(downloads.Path, recursive: true);

        var result = await bed.Get<Reconciler>().ReconcileAsync(bed.Library, [partial], Token);

        Assert.Equal(partial.Id, Assert.Single(result.Offline).Id);
        Assert.Empty(result.Missing);
    }

    private void Copy(string source, string relativePath)
    {
        var target = Path.Combine(downloads.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private async Task TrackAsync(string relativePath, MediaType type)
    {
        var file = new FileInfo(Path.Combine(downloads.Path, relativePath));
        var item = MediaItem.Discover(
            partial.Id,
            RelativePath.From(relativePath),
            type,
            file.Length,
            new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            fingerprint: null,
            DateTimeOffset.UtcNow);
        await bed.Library.Items.AddRangeAsync([item], Token);
    }
}
