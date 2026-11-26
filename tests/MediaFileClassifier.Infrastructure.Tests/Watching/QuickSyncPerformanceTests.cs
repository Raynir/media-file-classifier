using System.Diagnostics;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using MediaFileClassifier.UseCases.Rescan.StartRescan;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// A quick sync of an unchanged library is fast (E04-S03-T02 "Done when": 50,000 files within seconds on an SSD). A plain
/// run uses a small library and checks that nothing is found; with <c>MFC_PERF_TESTS=1</c> the library has 50,000 files and
/// the time is asserted. The time is written to the test output, and appended to the file
/// <c>MFC_QUICKSYNC_BENCHMARK_RESULTS</c> names, if set.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class QuickSyncPerformanceTests : IAsyncLifetime
{
    private const int FullCount = 50_000;
    private const int SmallCount = 500;
    private const int PerDirectory = 1_000;

    // When set, the time is appended to this file too.
    private const string ResultsVariable = "MFC_QUICKSYNC_BENCHMARK_RESULTS";

    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task QuickSync_OfAnUnchangedLibrary_FindsNothing_AndIsFast()
    {
        var count = TestPerformance.Enabled ? FullCount : SmallCount;
        await SeedAsync(count);

        var clock = Stopwatch.StartNew();
        var result = await bed.Sender.Send(new StartRescanCommand(Quick: true), Token);
        Assert.Equal(StartRescanOutcome.Started, result.Outcome);
        await bed.WaitUntilIdleAsync();
        var elapsed = clock.Elapsed;
        var line = $"Quick sync of {count} unchanged files: {elapsed.TotalMilliseconds:F0} ms.";
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results)
        {
            await File.AppendAllTextAsync(results, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}", Token);
        }

        var counts = (await bed.Sender.Send(new GetRescanSummaryQuery(result.JobId!.Value), Token))!.Counts!;
        Assert.Equal((0, 0, 0, 0, count), (counts.New, counts.Moved, counts.Missing, counts.Modified, counts.Unchanged));
        if (TestPerformance.Enabled)
        {
            Assert.True(elapsed < TimeSpan.FromSeconds(10), $"The quick sync took {elapsed.TotalMilliseconds:F0} ms.");
        }
    }

    // Indexed items whose files exist and haven't changed since.
    private async Task SeedAsync(int count)
    {
        var now = LibraryTestBed.Start;
        var items = new List<MediaItem>(count);
        var content = BitConverter.GetBytes(0);
        for (var i = 0; i < count; i++)
        {
            var directory = $"photos/{i / PerDirectory:D3}";
            Directory.CreateDirectory(Path.Combine(bed.Root, directory));
            var relative = $"{directory}/IMG_{i:D6}.jpg";
            var file = Path.Combine(bed.Root, relative);
            await File.WriteAllBytesAsync(file, content, Token);
            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file));
            var stored = DateTimeOffset.FromUnixTimeMilliseconds(written.ToUnixTimeMilliseconds());
            var item = MediaItem.Discover(bed.Folder.Id, RelativePath.From(relative), MediaType.Image, content.Length, stored, stored, (UInt128)i + 1, now);
            item.ChangeStatus(MediaItemStatus.Pending, now);
            item.ChangeStatus(MediaItemStatus.Indexed, now);
            items.Add(item);
        }

        foreach (var batch in items.Chunk(1_000))
        {
            await bed.Library.Items.AddRangeAsync(batch, Token);
        }
    }
}
