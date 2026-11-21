using System.Diagnostics;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;

namespace MediaFileClassifier.Infrastructure.Tests.Statistics;

/// <summary>
/// The library statistics against a real library database (E02-S05-T01): the counting rules, and the "Done when"
/// benchmark: all statistics in under 100 ms at 100k items. Times are written to the test output, and appended to the
/// file <c>MFC_STATISTICS_BENCHMARK_RESULTS</c> names, if set.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class LibraryStatisticsTests : IAsyncLifetime
{
    private const string ResultsVariable = "MFC_STATISTICS_BENCHMARK_RESULTS";

    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Statistics_CountByTypeAndStatus_LeaveOutExcluded_AndSizeLeavesOutMissing()
    {
        MediaItem[] items =
        [
            Item(1, MediaType.Image, 100, MediaItemStatus.Indexed),
            Item(2, MediaType.Image, 200, MediaItemStatus.Discovered),
            Item(3, MediaType.AnimatedImage, 300, MediaItemStatus.Pending),
            Item(4, MediaType.Video, 400, MediaItemStatus.Error),
            Item(5, MediaType.Video, 500, MediaItemStatus.Missing),
            Item(6, MediaType.Image, 600, MediaItemStatus.Excluded),
        ];
        await bed.Library.Items.AddRangeAsync(items, Token);

        var statistics = GetLibraryStatisticsQueryHandler.Summarize(await bed.Library.Items.CountByTypeAndStatusAsync(Token));

        Assert.Equal(new LibraryStatistics(Images: 2, AnimatedImages: 1, Videos: 2, Indexed: 1, Pending: 2, Errors: 1, Missing: 1, MediaSizeBytes: 1000), statistics);
        Assert.Equal(5, statistics.Total);
    }

    [Fact]
    public async Task Statistics_OfAnEmptyLibrary_AreAllZero()
    {
        var statistics = GetLibraryStatisticsQueryHandler.Summarize(await bed.Library.Items.CountByTypeAndStatusAsync(Token));

        Assert.Equal(LibraryStatistics.Empty, statistics);
    }

    [Fact]
    public async Task Statistics_At100kItems_ReturnInUnder100Ms()
    {
        const int ItemCount = 100_000;
        var types = Enum.GetValues<MediaType>();
        var statuses = Enum.GetValues<MediaItemStatus>();
        var items = Enumerable.Range(0, ItemCount)
            .Select(i => Item(i, types[i % types.Length], 1000 + i, statuses[i % statuses.Length]))
            .ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);

        // The first query warms up the connection pool and EF Core's compiled query, as the app does at startup.
        await bed.Library.Items.CountByTypeAndStatusAsync(Token);
        var times = new List<double>();
        LibraryStatistics? statistics = null;
        for (var run = 0; run < 5; run++)
        {
            var clock = Stopwatch.StartNew();
            statistics = GetLibraryStatisticsQueryHandler.Summarize(await bed.Library.Items.CountByTypeAndStatusAsync(Token));
            times.Add(clock.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        var median = times[times.Count / 2];
        var line = $"Library statistics at {ItemCount:N0} items: median {median:F1} ms, max {times[^1]:F1} ms.";
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results)
        {
            await File.AppendAllTextAsync(results, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}", Token);
        }

        var excluded = items.Count(item => item.Status == MediaItemStatus.Excluded);
        Assert.Equal(ItemCount - excluded, statistics!.Total);
        Assert.True(median < 100, line);
    }

    private MediaItem Item(int number, MediaType type, long size, MediaItemStatus status)
    {
        var item = MediaItem.Discover(bed.Folder.Id, RelativePath.From($"album/IMG_{number:D6}.jpg"), type, size, LibraryTestBed.Start, LibraryTestBed.Start, (UInt128)number + 1, LibraryTestBed.Start);
        if (status == MediaItemStatus.Indexed)
        {
            item.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
        }

        if (status != MediaItemStatus.Discovered)
        {
            item.ChangeStatus(status, LibraryTestBed.Start);
        }

        return item;
    }
}
