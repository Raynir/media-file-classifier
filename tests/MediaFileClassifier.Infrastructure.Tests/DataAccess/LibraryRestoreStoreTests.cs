using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// The library store's backup listing and the database swap behind "Restore backup" (E02-S06-T03).
/// </summary>
public sealed class LibraryRestoreStoreTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private string directory = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Backups => Path.Combine(directory, LibraryLayout.BackupsFolderName);

    private string Database => Path.Combine(directory, LibraryLayout.DatabaseFileName);

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        directory = bed.Library.Layout.RootDirectory;
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public void GetBackups_ListsMigrationBackupsNewestFirst_AndIgnoresTheRest()
    {
        Directory.CreateDirectory(Backups);
        foreach (var name in new[]
        {
            "library-v3-20261001-080000-000.db",
            "library-v12-20261102-093015-250.db",
            "library-damaged-20261103-100000-000.db",
            "library-v5-not-a-time.db",
            "notes.txt",
        })
        {
            File.WriteAllText(Path.Combine(Backups, name), string.Empty);
        }

        var backups = bed.Store.GetBackups(directory);

        Assert.Equal(
            [
                new LibraryBackup(Path.Combine(Backups, "library-v12-20261102-093015-250.db"), new DateTimeOffset(2026, 11, 2, 9, 30, 15, 250, TimeSpan.Zero), 12),
                new LibraryBackup(Path.Combine(Backups, "library-v3-20261001-080000-000.db"), new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), 3),
            ],
            backups);
    }

    [Fact]
    public void GetBackups_NoBackupsFolder_IsEmpty() => Assert.Empty(bed.Store.GetBackups(directory));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckBackup_UnreadableFile_IsAProblem_NotAnException(bool exists)
    {
        var path = Path.Combine(Backups, "library-v1-20261001-080000-000.db");
        if (exists)
        {
            Directory.CreateDirectory(Backups);
            await File.WriteAllBytesAsync(path, [.. Enumerable.Repeat((byte)0x5A, 4096)], Token);
        }

        var result = await bed.Store.CheckBackupAsync(path, Token);

        Assert.False(result.IsOk);
    }

    [Fact]
    public async Task ReplaceDamagedDatabase_LibraryOpen_IsInUse_AndChangesNothing()
    {
        var backup = await BackUpAsync();

        var failure = await Assert.ThrowsAsync<LibraryOpenException>(() => bed.Store.ReplaceDamagedDatabaseAsync(directory, backup, Token));

        Assert.Equal(LibraryOpenError.InUse, failure.Error);
        Assert.False(File.Exists(Database + ".restoring"));
        Assert.Single(Directory.EnumerateFiles(Backups));
    }

    [Fact]
    public async Task ReplaceDamagedDatabase_KeepsTheDatabaseAndItsWal_AndPutBackUndoesIt()
    {
        var backup = await BackUpAsync();
        await bed.Library.DisposeAsync();
        SqliteConnection.ClearAllPools();
        var original = await File.ReadAllBytesAsync(Database, Token);
        await File.WriteAllBytesAsync(Database + "-wal", [1, 2, 3], Token);
        await File.WriteAllBytesAsync(Database + "-shm", [4], Token);

        var damaged = await bed.Store.ReplaceDamagedDatabaseAsync(directory, backup, Token);

        Assert.Equal(Path.Combine(Backups, "library-damaged-20261030-120000-000.db"), damaged);
        Assert.Equal(original, await File.ReadAllBytesAsync(damaged, Token));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(damaged + "-wal", Token));
        Assert.Equal(await File.ReadAllBytesAsync(backup, Token), await File.ReadAllBytesAsync(Database, Token));
        Assert.False(File.Exists(Database + "-wal"));
        Assert.False(File.Exists(Database + "-shm"));
        Assert.False(File.Exists(Database + ".restoring"));

        await bed.Store.PutBackDamagedDatabaseAsync(directory, damaged, Token);

        Assert.Equal(original, await File.ReadAllBytesAsync(Database, Token));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Database + "-wal", Token));
        Assert.False(File.Exists(damaged));

        // The WAL is garbage here, so drop it before the bed reopens the library to close it.
        File.Delete(Database + "-wal");
        await bed.OpenAgainAsync();
    }

    [Fact]
    public async Task ReplaceDamagedDatabase_KeptNameTaken_FailsBeforeMovingAnything()
    {
        var backup = await BackUpAsync();
        await bed.Library.DisposeAsync();
        SqliteConnection.ClearAllPools();
        var original = await File.ReadAllBytesAsync(Database, Token);
        await File.WriteAllTextAsync(Path.Combine(Backups, "library-damaged-20261030-120000-000.db"), "taken", Token);

        await Assert.ThrowsAsync<IOException>(() => bed.Store.ReplaceDamagedDatabaseAsync(directory, backup, Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(Database, Token));
        Assert.False(File.Exists(Database + ".restoring"));
        await bed.OpenAgainAsync();
    }

    private async Task<string> BackUpAsync()
    {
        var path = Path.Combine(Backups, "library-v14-20261030-115900-000.db");
        await bed.Library.Maintenance.BackupAsync(path, Token);
        return path;
    }
}
