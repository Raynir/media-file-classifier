using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryMaintenance"/>: integrity check, online backup and optimization (E01-S03-T07).
/// </summary>
public sealed class LibraryMaintenanceTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static long OnDisk(string path) =>
        new FileInfo(path).Length + (File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : 0);

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task CheckIntegrityAsync_NewLibrary_IsOk()
    {
        var result = await bed.Library.Maintenance.CheckIntegrityAsync(Token);

        Assert.True(result.IsOk);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task BackupAsync_DuringWriteLoad_CanBeOpenedAsLibraryAndPassesIntegrityCheck()
    {
        var ingestion = Task.Run(() => bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, 10_000).Select(bed.NewItem)], Token), Token);
        while (await bed.Library.Items.CountAsync(Token) == 0)
        {
            await Task.Delay(5, Token);
        }

        var restored = Path.Combine(bed.Temporary.ParentDirectory, "restored" + LibraryLayout.Extension);
        await bed.Library.Maintenance.BackupAsync(Path.Combine(restored, LibraryLayout.DatabaseFileName), Token);
        var writtenDuringBackup = !ingestion.IsCompleted;
        await ingestion;

        await using var copy = await bed.Store.OpenAsync(restored, Token);
        Assert.True((await copy.Maintenance.CheckIntegrityAsync(Token)).IsOk);
        var count = await copy.Items.CountAsync(Token);
        Assert.True(writtenDuringBackup, "The import finished before the backup did, so the backup wasn't taken under a write load.");
        Assert.InRange(count, 1, 9_999);
        Assert.Equal(bed.Library.Library.Id, copy.Library.Id);
        TestContext.Current.TestOutputHelper?.WriteLine($"Backup holds {count} of 10000 items.");
    }

    [Fact]
    public async Task BackupAsync_DestinationExists_Throws()
    {
        var destination = Path.Combine(bed.Temporary.ParentDirectory, "backup.db");
        await File.WriteAllTextAsync(destination, "keep me", Token);

        await Assert.ThrowsAsync<IOException>(() => bed.Library.Maintenance.BackupAsync(destination, Token));
        Assert.Equal("keep me", await File.ReadAllTextAsync(destination, Token));
    }

    [Fact]
    public async Task OptimizeAsync_WithVacuum_ShrinksTheFileAfterALargeDelete_AndKeepsTheRest()
    {
        var items = Enumerable.Range(1, 6_000).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        var full = await bed.Library.Maintenance.GetSpaceAsync(Token);
        Assert.True(full.FreeBytes < full.FileBytes / 10, "A fresh library shouldn't have much free space.");
        await bed.Library.Items.DeleteFromFolderAsync(bed.Folder.Id, int.MaxValue, Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(100_001, 100).Select(bed.NewItem)], Token);

        var afterDelete = await bed.Library.Maintenance.GetSpaceAsync(Token);
        await bed.Library.Maintenance.OptimizeAsync(vacuum: true, Token);
        var compacted = await bed.Library.Maintenance.GetSpaceAsync(Token);

        Assert.True(afterDelete.FreeBytes > afterDelete.FileBytes / 2, $"Most of the file should be free after the delete: {afterDelete}.");
        Assert.True(compacted.FileBytes < afterDelete.FileBytes / 2, $"The file should have shrunk: {afterDelete.FileBytes} -> {compacted.FileBytes}.");
        Assert.True(compacted.FreeBytes < compacted.FileBytes / 10);
        Assert.True(compacted.AvailableDiskBytes > 0);
        Assert.Equal(100, await bed.Library.Items.CountAsync(Token));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
        TestContext.Current.TestOutputHelper?.WriteLine($"{afterDelete.FileBytes} -> {compacted.FileBytes} bytes");
    }

    [Fact]
    public async Task OptimizeAsync_WithVacuum_TruncatesTheWal_SoTheFilesOnDiskShrink()
    {
        var items = Enumerable.Range(1, 6_000).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        await bed.Library.Items.DeleteFromFolderAsync(bed.Folder.Id, int.MaxValue, Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(100_001, 100).Select(bed.NewItem)], Token);

        var path = bed.Library.Layout.DatabasePath;
        var before = OnDisk(path);
        await bed.Library.Maintenance.OptimizeAsync(vacuum: true, Token);

        var after = OnDisk(path);
        Assert.True(after < before / 2, $"On disk {before} -> {after} bytes.");
    }

    [Fact]
    public async Task TruncateWalAsync_BusyAtFirst_TriesAgainAndTruncatesTheWal()
    {
        // A checkpoint that can't run isn't an error: it reports busy in its result. A reader on an old snapshot makes
        // it busy here; the passive checkpointer holding the checkpoint lock does under load (MFC-1071).
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, 500).Select(bed.NewItem)], Token);
        var path = bed.Library.Layout.DatabasePath;
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        await using var checker = new SqliteConnection(connectionString);
        await checker.OpenAsync(Token);
        await using var reader = new SqliteConnection(connectionString);
        await reader.OpenAsync(Token);
        await using var begin = reader.CreateCommand();
        begin.CommandText = "BEGIN; SELECT count(*) FROM sqlite_master;";
        await begin.ExecuteNonQueryAsync(Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1_000, 500).Select(bed.NewItem)], Token);
        var waits = 0;

        var truncated = await LibraryMaintenance.TruncateWalAsync(
            checker,
            attempts: 50,
            async (_, token) =>
            {
                waits++;
                await using var end = reader.CreateCommand();
                end.CommandText = "COMMIT;";
                try
                {
                    await end.ExecuteNonQueryAsync(token);
                }
                catch (SqliteException)
                {
                    // Already ended: a later wait is for the checkpointer's own lock.
                }
            },
            Token);

        Assert.True(truncated);
        Assert.True(waits >= 1, "The first try should have been busy.");
        Assert.Equal(0, new FileInfo(path + "-wal").Length);
    }

    [Fact]
    public async Task TruncateWalAsync_AlwaysBusy_GivesUpAfterTheAttempts()
    {
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, 500).Select(bed.NewItem)], Token);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = bed.Library.Layout.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        await using var checker = new SqliteConnection(connectionString);
        await checker.OpenAsync(Token);
        await using var reader = new SqliteConnection(connectionString);
        await reader.OpenAsync(Token);
        await using var begin = reader.CreateCommand();
        begin.CommandText = "BEGIN; SELECT count(*) FROM sqlite_master;";
        await begin.ExecuteNonQueryAsync(Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1_000, 500).Select(bed.NewItem)], Token);
        var waits = 0;

        var truncated = await LibraryMaintenance.TruncateWalAsync(checker, attempts: 3, (_, _) => Task.FromResult(++waits), Token);

        Assert.False(truncated);
        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task OptimizeAsync_WithVacuum_WritesQueuedMeanwhileWaitAndAreKept()
    {
        var items = Enumerable.Range(1, 3_000).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);

        var vacuum = bed.Library.Maintenance.OptimizeAsync(vacuum: true, Token);
        var write = bed.Library.Items.DeleteAsync(items[0].Id, Token);
        await Task.WhenAll(vacuum, write);

        Assert.Equal(2_999, await bed.Library.Items.CountAsync(Token));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
    }

    [Fact]
    public async Task OptimizeAsync_WithVacuum_KeepsDataIntact()
    {
        var items = Enumerable.Range(1, 200).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        foreach (var item in items.Take(100))
        {
            await bed.Library.Items.DeleteAsync(item.Id, Token);
        }

        await bed.Library.Maintenance.OptimizeAsync(vacuum: true, Token);

        Assert.Equal(100, await bed.Library.Items.CountAsync(Token));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
    }
}
