using System.Diagnostics;
using MediaFileClassifier.Infrastructure.DataAccess.Database;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Stress test of the serialized writer (D072, E01-S03-T04): bulk ingestion from two jobs, user edits and UI reads at
/// the same time.
/// </summary>
public sealed class WriteConcurrencyTests
{
    private const int ItemsPerJob = 10_000;

    // Generous bounds, so a slow CI machine doesn't fail the test; typical values are a few milliseconds.
    private static readonly TimeSpan MaxReadLatency = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxEditLatency = TimeSpan.FromSeconds(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BulkWritesWithEditsAndReads_NoBusyErrorsAndNoLongWaits()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var edited = bed.NewItem(0);
        await bed.Library.Items.AddAsync(edited, Token);

        var ingestion = Task.WhenAll(
            Task.Run(() => bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, ItemsPerJob).Select(bed.NewItem)], Token), Token),
            Task.Run(() => bed.Library.Items.AddRangeAsync([.. Enumerable.Range(ItemsPerJob + 1, ItemsPerJob).Select(bed.NewItem)], Token), Token));

        var readLatencies = new List<TimeSpan>();
        var readers = Enumerable.Range(0, 3).Select(reader => Task.Run(
            async () =>
            {
                var random = new Random(reader);
                while (!ingestion.IsCompleted)
                {
                    var watch = Stopwatch.StartNew();
                    var count = await bed.Library.Items.CountAsync(Token);
                    await bed.Library.Items.GetPageAsync(random.Next(Math.Max(1, count)), 100, Token);
                    lock (readLatencies)
                    {
                        readLatencies.Add(watch.Elapsed);
                    }

                    await Task.Delay(5, Token);
                }
            },
            Token)).ToList();

        var editLatencies = new List<TimeSpan>();
        var editor = Task.Run(
            async () =>
            {
                while (!ingestion.IsCompleted)
                {
                    edited.SetFavorite(!edited.IsFavorite, DateTimeOffset.UtcNow);
                    var watch = Stopwatch.StartNew();
                    await bed.Library.Items.UpdateAsync(edited, Token);
                    editLatencies.Add(watch.Elapsed);
                    await Task.Delay(10, Token);
                }
            },
            Token);

        // Any SQLITE_BUSY, or any other failure, surfaces here.
        await Task.WhenAll([ingestion, editor, .. readers]);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"reads: n={readLatencies.Count} p50={Percentile(readLatencies, 0.5)} p99={Percentile(readLatencies, 0.99)} max={readLatencies.Max()}; "
            + $"edits: n={editLatencies.Count} p50={Percentile(editLatencies, 0.5)} max={editLatencies.Max()}");
        Assert.Equal((2 * ItemsPerJob) + 1, await bed.Library.Items.CountAsync(Token));
        Assert.Equal(edited.IsFavorite, (await bed.Library.Items.GetAsync(edited.Id, Token))?.IsFavorite);
        Assert.NotEmpty(readLatencies);
        Assert.NotEmpty(editLatencies);
        Assert.True(readLatencies.Max() < MaxReadLatency, $"A read took {readLatencies.Max()}.");
        Assert.True(editLatencies.Max() < MaxEditLatency, $"An edit took {editLatencies.Max()}.");
    }

    [Fact]
    public async Task Writer_CommitsDoNotCheckpoint()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await using var writer = await LibraryWriter.StartAsync(new LibraryConnectionFactory(bed.Library.Layout.DatabasePath), Token);
        long autoCheckpoint = -1;

        await writer.ExecuteWithoutTransactionAsync(
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_autocheckpoint;";
                autoCheckpoint = (long)(await command.ExecuteScalarAsync(token))!;
            },
            Token);

        Assert.Equal(0, autoCheckpoint);
    }

    [Fact]
    public async Task Writer_CheckpointsInTheBackground()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var database = new FileInfo(bed.Library.Layout.DatabasePath);
        var before = database.Length;

        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, 2_000).Select(bed.NewItem)], Token);

        // Only a checkpoint copies the new pages from the WAL into the database file while the library is open.
        var watch = Stopwatch.StartNew();
        while (database.Length <= before && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50, Token);
            database.Refresh();
        }

        Assert.True(database.Length > before, $"The database file is still {database.Length} bytes.");
    }

    [Fact]
    public async Task Writer_FailedWrite_DoesNotStopLaterWrites()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.Items.AddAsync(bed.NewItem(1), Token);

        await Assert.ThrowsAnyAsync<Exception>(() => bed.Library.Items.AddAsync(bed.NewItem(1), Token));
        await bed.Library.Items.AddAsync(bed.NewItem(2), Token);

        Assert.Equal(2, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task Writer_AfterDispose_RejectsWrites()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var connections = new LibraryConnectionFactory(bed.Library.Layout.DatabasePath);
        var writer = await LibraryWriter.StartAsync(connections, Token);
        await writer.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.ExecuteAsync((db, token) => Task.CompletedTask, Token));
    }

    [Fact]
    public async Task Writer_QueuedWriteCancelled_FailsAtOnceAndNeverRuns()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await using var writer = await LibraryWriter.StartAsync(new LibraryConnectionFactory(bed.Library.Layout.DatabasePath), Token);
        var gate = new TaskCompletionSource();
        var blocking = writer.ExecuteAsync((db, token) => gate.Task, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var ran = false;
        var queued = writer.ExecuteAsync(
            (db, token) =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(blocking.IsCompleted);
        gate.SetResult();
        await blocking;
        await writer.ExecuteAsync((db, token) => Task.CompletedTask, Token);
        Assert.False(ran);
    }

    [Fact]
    public async Task Writer_WriteQueuesAnotherWrite_FailsInsteadOfDeadlocking()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await using var writer = await LibraryWriter.StartAsync(new LibraryConnectionFactory(bed.Library.Layout.DatabasePath), Token);

        var nested = writer.ExecuteAsync((db, token) => writer.ExecuteAsync((inner, innerToken) => Task.CompletedTask, token), Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => nested.WaitAsync(TimeSpan.FromSeconds(10), Token));
        await writer.ExecuteAsync((db, token) => Task.CompletedTask, Token);
    }

    private static TimeSpan Percentile(List<TimeSpan> values, double percentile)
    {
        var sorted = values.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * percentile))];
    }
}
