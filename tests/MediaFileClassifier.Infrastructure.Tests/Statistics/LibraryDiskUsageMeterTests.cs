using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Statistics;

/// <summary>
/// Tests for <see cref="LibraryDiskUsageMeter"/> (E02-S05-T03): the sizes match the files on disk, and they're
/// measured in the background and cached.
/// </summary>
public sealed class LibraryDiskUsageMeterTests : IDisposable
{
    private readonly TemporaryFolder folder = new();
    private readonly TestClock clock = new(LibraryTestBed.Start);
    private readonly LibraryLayout layout;

    public LibraryDiskUsageMeterTests() => layout = LibraryLayout.FromDirectory(folder.Combine("Family.mfc"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Measure_SumsTheDatabaseThumbnailsAndBackups_AndIgnoresOtherFiles()
    {
        Write(layout.DatabasePath, 1000);
        Write(layout.DatabasePath + "-wal", 200);
        Write(layout.DatabasePath + "-shm", 30);
        Write(Path.Combine(layout.ThumbnailsDirectory, "ab", "x-256.webp"), 4000);
        Write(Path.Combine(layout.ThumbnailsDirectory, "ab", "x-frame-0.webp"), 500);
        Write(Path.Combine(layout.ThumbnailsDirectory, "cd", "y-512.webp"), 6000);
        Write(Path.Combine(layout.BackupsDirectory, "library-v14-20261120-101010-000.db"), 700);
        Write(layout.LockFilePath, 9);

        var usage = await new LibraryDiskUsageMeter(clock).MeasureAsync(layout, TimeSpan.Zero, Token);

        Assert.Equal(new LibraryDiskUsage(1230, 10_500, 700), usage);
        Assert.Equal(12_430, usage.TotalBytes);
        var actual = Directory.EnumerateFiles(layout.RootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(LibraryLayout.LockFileName, StringComparison.Ordinal))
            .Sum(path => new FileInfo(path).Length);
        Assert.Equal(actual, usage.TotalBytes);
    }

    [Fact]
    public async Task Measure_OfAMissingLibraryFolder_IsZero()
    {
        var usage = await new LibraryDiskUsageMeter(clock).MeasureAsync(layout, TimeSpan.Zero, Token);

        Assert.Equal(new LibraryDiskUsage(0, 0, 0), usage);
    }

    [Fact]
    public async Task Measure_IsCachedForMaxAge_ThenMeasuredAgain()
    {
        var meter = new LibraryDiskUsageMeter(clock);
        Write(layout.DatabasePath, 100);
        Assert.Equal(100, (await meter.MeasureAsync(layout, TimeSpan.FromSeconds(30), Token)).DatabaseBytes);

        Write(layout.DatabasePath, 300);
        clock.Tick();
        Assert.Equal(100, (await meter.MeasureAsync(layout, TimeSpan.FromSeconds(30), Token)).DatabaseBytes);

        for (var i = 0; i < 30; i++)
        {
            clock.Tick();
        }

        Assert.Equal(300, (await meter.MeasureAsync(layout, TimeSpan.FromSeconds(30), Token)).DatabaseBytes);
    }

    [Fact]
    public async Task Measure_CalledFromManyThreadsAtOnce_GivesEveryCallerTheSizes()
    {
        var meter = new LibraryDiskUsageMeter(clock);
        for (var i = 0; i < 2000; i++)
        {
            Write(Path.Combine(layout.ThumbnailsDirectory, $"{i % 100:D2}", $"{i}-256.webp"), 10);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => meter.MeasureAsync(layout, TimeSpan.Zero, Token), Token)));

        Assert.All(results, usage => Assert.Equal(20_000, usage.ThumbnailsBytes));
    }

    public void Dispose() => folder.Dispose();

    private static void Write(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
