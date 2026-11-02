using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Discovery;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Files;
using MediaFileClassifier.Infrastructure.Tests.Media;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Discovery;

/// <summary>
/// Tests for <see cref="FolderDiscovery"/> against real files and a real library (E03-S02).
/// </summary>
public sealed class FolderDiscoveryTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> links = [];
    private LibraryTestBed bed = null!;
    private FolderDiscovery discovery = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(root);
        bed = await LibraryTestBed.CreateAsync();
        discovery = new FolderDiscovery(
            new FileSystemMediaFileSource(),
            FormatTestSupport.CreateRegistry(),
            bed.Clock,
            NullLogger<FolderDiscovery>.Instance)
        {
            // Batches by count only, so the tests don't depend on how fast the disk is.
            FlushInterval = TimeSpan.MaxValue,
        };
    }

    [Fact]
    public async Task DiscoverAsync_RegistersSupportedMediaAsDiscovered()
    {
        var photo = Copy(SampleMedia.JpegWithExif, "2024/trip/photo.jpg");
        var lastWrite = new DateTime(2024, 5, 1, 12, 0, 0, 123, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(photo, lastWrite);
        Copy(SampleMedia.ShortVideo, "clip.mp4");
        Write("notes.txt", "not media");
        Write("fake.jpg", "text with a photo's extension");
        var folder = await AddFolderAsync(new FolderRules());

        var result = await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: Token);

        Assert.Equal(2, result.FilesFound);
        Assert.Equal(2, result.ItemsRegistered);
        Assert.Equal(1, result.Unsupported);
        Assert.Equal(3, result.FoldersScanned);
        var items = await ItemsAsync(folder);
        Assert.Equal(["2024/trip/photo.jpg", "clip.mp4"], items.Select(item => item.Path.Value).Order(StringComparer.Ordinal));
        var item = items.Single(item => item.Path.Value == "2024/trip/photo.jpg");
        Assert.Equal(MediaItemStatus.Discovered, item.Status);
        Assert.Equal(MediaType.Image, item.MediaType);
        Assert.Equal(new FileInfo(photo).Length, item.SizeBytes);
        Assert.Equal(new DateTimeOffset(lastWrite), item.FileModifiedAt);
        Assert.Null(item.Hashes.Fingerprint);
        Assert.Equal(MediaType.Video, items.Single(item => item.Path.Value == "clip.mp4").MediaType);
    }

    [Fact]
    public async Task DiscoverAsync_RespectsRulesAndHiddenSetting()
    {
        Copy(SampleMedia.JpegWithExif, "keep.jpg");
        Copy(SampleMedia.JpegWithExif, "raw/skip.jpg");
        Copy(SampleMedia.JpegWithExif, "sub/skip.tmp.jpg");
        var hidden = Copy(SampleMedia.JpegWithExif, "hidden.jpg");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var folder = await AddFolderAsync(new FolderRules { ExcludePatterns = ["raw", "*.tmp.jpg"] });

        await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: Token);

        Assert.Equal(["keep.jpg"], (await ItemsAsync(folder)).Select(item => item.Path.Value));
    }

    [Fact]
    public async Task DiscoverAsync_IncludeHidden_RegistersHiddenFiles()
    {
        var hidden = Copy(SampleMedia.JpegWithExif, "hidden.jpg");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var folder = await AddFolderAsync(new FolderRules { IncludeHidden = true });

        await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: Token);

        Assert.Equal(["hidden.jpg"], (await ItemsAsync(folder)).Select(item => item.Path.Value));
    }

    [Fact]
    public async Task DiscoverAsync_RegistersInBatches_SoItemsShowUpWhileScanning()
    {
        for (var i = 0; i < 250; i++)
        {
            Copy(SampleMedia.JpegWithExif, $"album/{i / 50}/IMG_{i:D4}.jpg");
        }

        var folder = await AddFolderAsync(new FolderRules());
        var progress = new RecordingProgress();

        var result = await discovery.DiscoverAsync(bed.Library, folder, progress, Token);

        Assert.Equal(250, result.ItemsRegistered);
        Assert.Equal([100, 200, 250], progress.Reports.Select(report => report.ItemsRegistered).Distinct().Where(count => count > 0));
        Assert.Equal(7, progress.Reports[^1].FoldersScanned);
        Assert.Equal(250, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task DiscoverAsync_FlushInterval_WritesPartialBatches()
    {
        for (var i = 0; i < 3; i++)
        {
            Copy(SampleMedia.JpegWithExif, $"IMG_{i:D4}.jpg");
        }

        var folder = await AddFolderAsync(new FolderRules());
        var progress = new RecordingProgress();

        var eager = new FolderDiscovery(
            new FileSystemMediaFileSource(),
            FormatTestSupport.CreateRegistry(),
            bed.Clock,
            NullLogger<FolderDiscovery>.Instance)
        {
            FlushInterval = TimeSpan.Zero,
        };

        await eager.DiscoverAsync(bed.Library, folder, progress, Token);

        Assert.Equal([1, 2, 3], progress.Reports.Select(report => report.ItemsRegistered).Distinct().Where(count => count > 0));
    }

    [Fact]
    public async Task DiscoverAsync_Cancelled_KeepsTheBatchesAlreadyWritten()
    {
        for (var i = 0; i < 250; i++)
        {
            Copy(SampleMedia.JpegWithExif, $"IMG_{i:D4}.jpg");
        }

        var folder = await AddFolderAsync(new FolderRules());
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(report =>
        {
            if (report.ItemsRegistered == 100)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.DiscoverAsync(bed.Library, folder, progress, cancellation.Token));

        Assert.Equal(100, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task DiscoverAsync_ReportsSkippedLinks()
    {
        Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        var link = Path.Combine(root, "a", "loop");
        FolderScanTests.CreateFolderLink(link, root);
        links.Add(link);
        var folder = await AddFolderAsync(new FolderRules());

        var result = await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: Token);

        Assert.Equal(["a/loop"], result.SkippedLinks);
        Assert.Equal(1, result.ItemsRegistered);
    }

    [Fact]
    public async Task DiscoverAsync_MissingFolder_Throws()
    {
        var folder = IndexedFolder.Create(bed.Library.Library.Id, root, Path.Combine(root, "missing"), bed.Clock.Tick());

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => discovery.DiscoverAsync(bed.Library, folder, cancellationToken: Token));
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        foreach (var link in links)
        {
            Directory.Delete(link);
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<IndexedFolder> AddFolderAsync(FolderRules rules)
    {
        var folder = IndexedFolder.Create(bed.Library.Library.Id, root, root, bed.Clock.Tick());
        folder.ChangeRules(rules);
        await bed.Library.Folders.AddAsync(folder, Token);
        return folder;
    }

    private async Task<List<MediaItem>> ItemsAsync(IndexedFolder folder) =>
        [.. (await bed.Library.Items.GetPageAsync(0, 1000, Token)).Where(item => item.FolderId == folder.Id)];

    private string Copy(string source, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(source, path);
        return path;
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Records progress reports as they're made, on the scanning thread, unlike <see cref="Progress{T}"/>.
    /// </summary>
    /// <param name="onReport">Called with each report.</param>
    private sealed class RecordingProgress(Action<DiscoveryProgress>? onReport = null) : IProgress<DiscoveryProgress>
    {
        public List<DiscoveryProgress> Reports { get; } = [];

        public void Report(DiscoveryProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }
}
