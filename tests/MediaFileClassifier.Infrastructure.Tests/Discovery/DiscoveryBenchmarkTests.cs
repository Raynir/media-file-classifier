using System.Diagnostics;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Discovery;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Media;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Discovery;

/// <summary>
/// Measures discovery on generated trees (E03-S02-T05). It only runs when <c>MFC_DISCOVERY_BENCHMARK_DIR</c> names a
/// folder to build the trees in, so a disk can be chosen; the results are appended to <c>results.txt</c> there.
/// Results are recorded in docs/file-discovery.md.
/// </summary>
public sealed class DiscoveryBenchmarkTests
{
    private const string DirectoryVariable = "MFC_DISCOVERY_BENCHMARK_DIR";
    private const int FilesPerFolder = 100;

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public async Task Discovery(int fileCount)
    {
        var baseDirectory = Environment.GetEnvironmentVariable(DirectoryVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(baseDirectory), $"Set {DirectoryVariable} to run the discovery benchmark.");
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(baseDirectory, $"tree-{fileCount}");
        var created = EnsureTree(root, fileCount);

        await using var bed = await LibraryTestBed.CreateAsync();
        var source = new FileSystemMediaFileSource();
        var discovery = new FolderDiscovery(source, FormatTestSupport.CreateRegistry(), TimeProvider.System, NullLogger<FolderDiscovery>.Instance);
        var folder = IndexedFolder.Create(bed.Library.Library.Id, root, root, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(folder, token);

        var clock = Stopwatch.StartNew();
        var listed = await source.ScanAsync(LocalFolder.ToHandle(root), new FolderScanOptions(), token)
            .CountAsync(entry => entry.Kind == FolderScanEntryKind.File, token);
        var scan = clock.Elapsed;

        clock.Restart();
        var first = await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: token);
        var discover = clock.Elapsed;

        clock.Restart();
        var again = await discovery.DiscoverAsync(bed.Library, folder, cancellationToken: token);
        var rescan = clock.Elapsed;

        Assert.Equal(fileCount, listed);
        Assert.Equal(fileCount, first.ItemsRegistered);
        Assert.Equal(fileCount, again.Unchanged);
        Assert.Equal(fileCount, await bed.Library.Items.CountAsync(token));
        await File.AppendAllTextAsync(
            Path.Combine(baseDirectory, "results.txt"),
            $"{DateTimeOffset.Now:u} {root} files={fileCount} treeCreatedNow={created} scanOnly={scan.TotalSeconds:F2}s "
                + $"discover={discover.TotalSeconds:F2}s ({fileCount / discover.TotalSeconds:F0}/s) rescan={rescan.TotalSeconds:F2}s{Environment.NewLine}",
            token);
    }

    // Small files that start like a real JPEG, 100 to a folder. The tree is kept between runs, so a later run reads
    // files that are already known to the OS (and to the virus scanner).
    private static bool EnsureTree(string root, int fileCount)
    {
        var marker = Path.Combine(root, ".complete");
        if (File.Exists(marker))
        {
            return false;
        }

        var content = File.ReadAllBytes(SampleMedia.JpegWithExif).AsSpan(0, 4096).ToArray();
        for (var i = 0; i < fileCount; i++)
        {
            var directory = Path.Combine(root, $"folder-{i / FilesPerFolder:D4}");
            if (i % FilesPerFolder == 0)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(Path.Combine(directory, $"IMG_{i:D6}.jpg"), content);
        }

        File.WriteAllText(marker, string.Empty);
        File.SetAttributes(marker, FileAttributes.Hidden);
        return true;
    }
}
