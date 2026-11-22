using System.Diagnostics;
using System.Security.Cryptography;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Exclusions;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;

namespace MediaFileClassifier.Infrastructure.Tests.Exclusions;

/// <summary>
/// Removing 1,000 analysed items is fast, and their files stay as they were (E02-S10-T01 "Done when"). The marking is
/// what the user waits for; the cleanup runs as a background job. Times are written to the test output, and appended
/// to the file <c>MFC_EXCLUDE_BENCHMARK_RESULTS</c> names, if set.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class ExcludeItemsBenchmarkTests : IAsyncLifetime
{
    private const int ItemCount = 1000;

    // When set, the times are appended to this file too.
    private const string ResultsVariable = "MFC_EXCLUDE_BENCHMARK_RESULTS";

    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Remove_1000AnalysedItems_IsFast_CleansUpInTheBackground_AndLeavesTheFilesUntouched()
    {
        var items = await SeedAsync();
        var before = SnapshotFiles();

        var clock = Stopwatch.StartNew();
        var result = await bed.Sender.Send(new ExcludeItemsCommand([.. items.Select(item => item.Id)]), Token);
        var marked = clock.Elapsed;
        await bed.WaitUntilIdleAsync();
        var cleaned = clock.Elapsed;
        var line = $"Removing {ItemCount} items: marked in {marked.TotalMilliseconds:F0} ms, cleaned up after {cleaned.TotalMilliseconds:F0} ms.";
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results)
        {
            await File.AppendAllTextAsync(results, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}", Token);
        }

        Assert.Equal(new ExcludeItemsResult(ItemCount, CleanupQueued: true), result);
        if (TestPerformance.Enabled)
        {
            Assert.True(marked < TimeSpan.FromSeconds(3), $"Marking took {marked.TotalMilliseconds:F0} ms.");
        }

        Assert.All(await bed.Library.Items.GetByIdsAsync([.. items.Select(item => item.Id)], Token), item => Assert.True(LibraryExclusions.IsRemovedByUser(item)));
        Assert.Equal(0, await bed.Library.Items.CountAsync(new MediaItemFilter(), Token));
        Assert.Empty(await bed.Library.Embeddings.GetModelsInUseAsync(Token));
        var metadata = await bed.Library.Metadata.GetManyAsync([.. items.Select(item => item.Id)], Token);
        Assert.All(metadata.Values, entry => Assert.Empty(entry.Runs));
        Assert.Equal(before, SnapshotFiles());
    }

    // Indexed items whose files exist, each with an analysis run, an analyzer state and an embedding.
    private async Task<IReadOnlyList<MediaItem>> SeedAsync()
    {
        var now = LibraryTestBed.Start;
        var model = bed.LibraryBed.Model;
        var items = new List<MediaItem>();
        Directory.CreateDirectory(Path.Combine(bed.Root, "photos"));
        for (var i = 0; i < ItemCount; i++)
        {
            var relative = $"photos/IMG_{i:D5}.jpg";
            var file = Path.Combine(bed.Root, relative);
            await File.WriteAllBytesAsync(file, BitConverter.GetBytes(i), Token);
            var item = MediaItem.Discover(bed.Folder.Id, RelativePath.From(relative), MediaType.Image, 4, now, now, (UInt128)i + 1, now);
            item.ChangeStatus(MediaItemStatus.Pending, now);
            item.ChangeStatus(MediaItemStatus.Indexed, now);
            items.Add(item);
        }

        await bed.Library.Items.AddRangeAsync(items, Token);
        await bed.Library.Embeddings.ReplaceRangeAsync(
            [.. items.Select(item => new ItemEmbeddings(item.Id, model.Id, [Embedding.Create(item.Id, model, EmbeddingKind.Image, [0.5f, 0.5f, 0.5f, 0.5f], now)]))],
            Token);
        foreach (var item in items)
        {
            var run = AnalysisRun.Start(item.Id, AnalyzerKeys.Describer, model, "p1", now);
            run.Complete(now);
            await bed.Library.AnalyzerStates.ApplyRunAsync(
                run,
                [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText("Lighthouse"), null, now)],
                [TagAssignment.Ai(run, TagName.From("lamp"), null, now)],
                1,
                AnalyzerState.Done(item.Id, AnalyzerKeys.Describer, run.Id, now),
                Token);
        }

        return items;
    }

    private Dictionary<string, (long Length, DateTime Written, string Hash)> SnapshotFiles() =>
        Directory.EnumerateFiles(bed.Root, "*", SearchOption.AllDirectories).ToDictionary(
            path => path,
            path => (new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
}
