using System.Diagnostics;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// Measures the import pipeline's throughput at several CPU pool sizes (E03-S07-T01). It only runs when
/// <c>MFC_IMPORT_BENCHMARK_DIR</c> names a folder to build the photos in; the results are appended to <c>results.txt</c>
/// there. Results are recorded in docs/import-pipeline.md.
/// </summary>
public sealed class ImportBenchmarkTests
{
    private const string DirectoryVariable = "MFC_IMPORT_BENCHMARK_DIR";

    [Theory]
    [InlineData(2_000, 1)]
    [InlineData(2_000, 2)]
    [InlineData(2_000, 4)]
    [InlineData(2_000, 8)]
    public async Task Import(int fileCount, int cpu)
    {
        var baseDirectory = Environment.GetEnvironmentVariable(DirectoryVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(baseDirectory), $"Set {DirectoryVariable} to run the import benchmark.");
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(baseDirectory, $"photos-{fileCount}");
        EnsurePhotos(root, fileCount);

        await using var bed = await ImportTestBed.CreateAsync(cpu, root);
        var clock = Stopwatch.StartNew();
        await bed.ImportAsync();
        while ((await bed.Library.Items.GetByStatusAsync(MediaItemStatus.Pending, bed.Folder.Id, token)).Count < fileCount)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(20), "Timed out waiting for the import.");
            await Task.Delay(50, token);
        }

        var registered = clock.Elapsed;
        await bed.WaitUntilIdleAsync(TimeSpan.FromMinutes(20));
        var hashed = clock.Elapsed;

        Assert.Equal(fileCount, bed.Registered.Received.Count);
        await File.AppendAllTextAsync(
            Path.Combine(baseDirectory, "results.txt"),
            $"{DateTimeOffset.Now:u} files={fileCount} cpu={cpu} io=4 registered={registered.TotalSeconds:F1}s ({fileCount / registered.TotalSeconds:F0}/s) "
                + $"withFullHash={hashed.TotalSeconds:F1}s ({fileCount / hashed.TotalSeconds:F0}/s){Environment.NewLine}",
            token);
    }

    // Distinct 640×480 photo-like JPEGs, 100 to a folder, kept between runs.
    private static void EnsurePhotos(string root, int fileCount)
    {
        var marker = Path.Combine(root, ".complete");
        if (File.Exists(marker))
        {
            return;
        }

        var photo = File.ReadAllBytes(SampleMedia.JpegWithExif);
        for (var i = 0; i < fileCount; i++)
        {
            var directory = Path.Combine(root, $"folder-{i / 100:D3}");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"IMG_{i:D6}.jpg"), [.. photo, .. BitConverter.GetBytes(i)]);
        }

        File.WriteAllText(marker, string.Empty);
        File.SetAttributes(marker, FileAttributes.Hidden);
    }
}
