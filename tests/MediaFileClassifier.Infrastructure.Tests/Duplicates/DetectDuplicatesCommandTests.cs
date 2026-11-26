using System.Diagnostics;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Duplicates;
using MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// The full duplicate detection pass as a background job, with a real library and job system (E12-S01-T03).
/// </summary>
public sealed class DetectDuplicatesCommandTests : IAsyncLifetime
{
    // A file to append the measured time to, e.g. when measuring by hand.
    private const string ResultsVariable = "MFC_DUPLICATES_BENCHMARK_RESULTS";

    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task FullPass_100kItems_CompletesAsABackgroundJob()
    {
        // 100,000 items: the first 20,000 are 10,000 pairs of copies, and 300 groups exist that no longer match.
        const int ItemCount = 100_000;
        await AddItemsAsync(ItemCount, i => i < 20_000 ? (UInt128)(i / 2) + 1 : (UInt128)i + 1_000_000);
        await RunSqlAsync("""DELETE FROM "duplicate_dirty_hash" """);
        await AddStaleGroupsAsync(300);

        var clock = Stopwatch.StartNew();
        var result = await bed.Sender.Send(new DetectDuplicatesCommand(), Token);
        var queuedIn = clock.Elapsed;
        await bed.WaitUntilIdleAsync();
        var line = $"Duplicate detection at {ItemCount:N0} items: {result.Hashes:N0} hashes queued in {queuedIn.TotalMilliseconds:F0} ms, job done in {clock.Elapsed.TotalSeconds:F1} s.";
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable(ResultsVariable) is { Length: > 0 } results)
        {
            await File.AppendAllTextAsync(results, $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}", Token);
        }

        Assert.Equal(DetectDuplicatesOutcome.Started, result.Outcome);
        Assert.Equal(10_300, result.Hashes);
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(result.JobId!.Value, Token))!.State);
        Assert.Equal(10_000, await bed.Library.Duplicates.CountAsync(DuplicateGroupState.Open, Token));
        Assert.Equal(0, await bed.Library.Duplicates.CountQueuedAsync(Token));
        if (TestPerformance.Enabled)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), line);
        }
    }

    [Fact]
    public async Task FullPass_Cancelled_LeavesConsistentGroupsAndTheRestQueued()
    {
        await AddItemsAsync(12_000, i => (UInt128)(i / 2) + 1);
        await RunSqlAsync("""DELETE FROM "duplicate_dirty_hash" """);

        var result = await bed.Sender.Send(new DetectDuplicatesCommand(), Token);
        await bed.Jobs.Current!.CancelJobAsync(result.JobId!.Value, Token);
        await bed.WaitUntilIdleAsync();

        // Every batch is one transaction: what was done is whole, and the rest is still queued for the next pass.
        var done = await bed.Library.Duplicates.CountAsync(null, Token);
        Assert.Equal(6_000 - done, await bed.Library.Duplicates.CountQueuedAsync(Token));
        while ((await bed.Library.Duplicates.ReconcileAsync(DuplicateGroupMaintainer.BatchSize, DateTimeOffset.UtcNow, Token)).Hashes > 0)
        {
        }

        Assert.Equal(6_000, await bed.Library.Duplicates.CountAsync(null, Token));
    }

    [Fact]
    public async Task FullPass_NoSharedHashes_QueuesNothing()
    {
        await AddItemsAsync(10, i => (UInt128)i + 1);
        await RunSqlAsync("""DELETE FROM "duplicate_dirty_hash" """);

        var result = await bed.Sender.Send(new DetectDuplicatesCommand(), Token);

        Assert.Equal(DetectDuplicatesOutcome.NothingToDo, result.Outcome);
        Assert.Null(result.JobId);
    }

    [Fact]
    public async Task FullPass_WhileOneIsUnfinished_IsBusy()
    {
        await AddItemsAsync(2, _ => 7);
        await bed.Library.Jobs.AddJobAsync(
            new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = DuplicateStages.DetectJob, Description = "Find duplicates", CreatedAt = DateTimeOffset.UtcNow },
            [],
            Token);

        var result = await bed.Sender.Send(new DetectDuplicatesCommand(), Token);

        Assert.Equal(DetectDuplicatesOutcome.Busy, result.Outcome);
    }

    private async Task AddItemsAsync(int count, Func<int, UInt128> hash)
    {
        var items = Enumerable.Range(0, count).Select(i =>
        {
            var item = bed.LibraryBed.NewItem(i + 1);
            item.SetHashes(new FileHashes { ContentHash = hash(i) });
            return item;
        }).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
    }

    private async Task AddStaleGroupsAsync(int count)
    {
        await using var db = await new LibraryConnectionFactory(bed.LibraryBed.Temporary.DatabasePath).CreateContextAsync(tracking: true, cancellationToken: Token);
        db.DuplicateGroups.AddRange(Enumerable.Range(0, count).Select(i => DuplicateGroup.CreateExact((UInt128)i + 5_000_000, DateTimeOffset.UtcNow)));
        await db.SaveChangesAsync(Token);
    }

    private async Task RunSqlAsync(string sql)
    {
        await using var db = await new LibraryConnectionFactory(bed.LibraryBed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
        await db.Database.ExecuteSqlRawAsync(sql, Token);
    }
}
