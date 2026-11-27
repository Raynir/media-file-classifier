using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Libraries.OpenLibrary;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;
using MediaFileClassifier.UseCases.Maintenance.RestoreBackup;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Maintenance;

/// <summary>
/// The integrity report and the backup restore with a deliberately corrupted library database (E02-S06-T03).
/// </summary>
public sealed class IntegrityRestoreTests : IAsyncLifetime
{
    private const string BackupName = "library-v14-20261030-115900-000.db";

    private readonly LibrarySession session = new();
    private LibraryTestBed bed = null!;
    private ServiceProvider services = null!;
    private string directory = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    private string BackupPath => Path.Combine(directory, LibraryLayout.BackupsFolderName, BackupName);

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        directory = bed.Library.Layout.RootDirectory;
        await session.SetCurrentAsync(bed.Library);
        var settings = new ImportTestSettings();
        services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ILibraryStore>(bed.Store)
            .AddSingleton<ILibrarySession>(session)
            .AddMediatR(config => config.RegisterServicesFromAssembly(typeof(UseCasesModule).Assembly))

            // Without the folder check, which needs the file system services.
            .AddTransient<IRequestHandler<OpenLibraryCommand, OpenLibraryResult>>(_ => new OpenLibraryCommandHandler(
                bed.Store,
                session,
                settings,
                bed.Clock,
                NullLogger<OpenLibraryCommandHandler>.Instance))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync();
        await services.DisposeAsync();
        await bed.DisposeAsync();
    }

    [Fact]
    public async Task Check_HealthyLibrary_IsOk()
    {
        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);

        Assert.True(report.IsOk);
        Assert.Null(report.Backup);
    }

    [Fact]
    public async Task Check_CorruptedDatabase_ReportsTheDamage_AndTheNewestBackup()
    {
        await AddItemsAsync(1, 30);
        await bed.Library.Maintenance.BackupAsync(BackupPath, Token);
        await CorruptAsync();

        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);

        Assert.False(report.IsOk);
        Assert.NotEmpty(report.Problems);
        Assert.Equal(BackupPath, report.Backup?.Path);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 11, 59, 0, TimeSpan.Zero), report.Backup!.CreatedAt);
    }

    [Fact]
    public async Task Check_CorruptedDatabase_WithoutABackup_HasNoBackup()
    {
        await AddItemsAsync(1, 30);
        await CorruptAsync();

        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);

        Assert.False(report.IsOk);
        Assert.Null(report.Backup);
    }

    [Fact]
    public async Task Restore_BringsBackTheBackup_KeepsTheDamagedDatabase_AndReopensTheLibrary()
    {
        await AddItemsAsync(1, 30);
        await bed.Library.Maintenance.BackupAsync(BackupPath, Token);
        await AddItemsAsync(31, 5);
        await CorruptAsync();
        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);
        bed.Clock.Tick();

        var result = await Sender.Send(new RestoreIntegrityBackupCommand(report.Backup!), Token);

        Assert.True(result.Succeeded);
        Assert.True(result.Report!.IsOk);
        Assert.Equal(Path.Combine(directory, LibraryLayout.BackupsFolderName, "library-damaged-20261030-120001-000.db"), result.DamagedPath);
        Assert.False((await bed.Store.CheckBackupAsync(result.DamagedPath!, Token)).IsOk);
        Assert.True(File.Exists(BackupPath));
        var reopened = Assert.IsAssignableFrom<IOpenLibrary>(session.Current);
        Assert.NotSame(bed.Library, reopened);
        Assert.Equal(30, (await reopened.Items.GetPageAsync(0, 100, Token)).Count);

        // The kept database isn't offered as a backup.
        Assert.Equal([BackupPath], bed.Store.GetBackups(directory).Select(backup => backup.Path));
    }

    [Fact]
    public async Task Restore_DamagedBackup_ChangesNothing()
    {
        await AddItemsAsync(1, 30);
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        await File.WriteAllBytesAsync(BackupPath, [.. Enumerable.Repeat((byte)0x5A, 8192)], Token);
        await CorruptAsync();
        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);

        var result = await Sender.Send(new RestoreIntegrityBackupCommand(report.Backup!), Token);

        Assert.Equal(RestoreBackupFailure.BackupDamaged, result.Failure);
        Assert.Same(bed.Library, session.Current);
        Assert.False(Directory.EnumerateFiles(Path.GetDirectoryName(BackupPath)!, "library-damaged-*").Any());
    }

    [Fact]
    public async Task Restore_RestoredLibraryDoesNotOpen_PutsTheDamagedDatabaseBack()
    {
        await AddItemsAsync(1, 30);

        // A valid SQLite database that isn't a library: it passes the check but doesn't open.
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        await using (var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = BackupPath, Pooling = false }.ToString()))
        {
            await other.OpenAsync(Token);
            await using var command = other.CreateCommand();
            command.CommandText = "CREATE TABLE notes (text TEXT)";
            await command.ExecuteNonQueryAsync(Token);
        }

        await CorruptAsync();
        var report = await Sender.Send(new CheckLibraryIntegrityQuery(), Token);

        var result = await Sender.Send(new RestoreIntegrityBackupCommand(report.Backup!), Token);

        Assert.Equal(RestoreBackupFailure.OpenFailed, result.Failure);
        var reopened = Assert.IsAssignableFrom<IOpenLibrary>(session.Current);
        Assert.Equal(30, (await reopened.Items.GetPageAsync(0, 100, Token)).Count);
        Assert.False((await Sender.Send(new CheckLibraryIntegrityQuery(), Token)).IsOk);
        Assert.False(Directory.EnumerateFiles(Path.GetDirectoryName(BackupPath)!, "library-damaged-*").Any());
    }

    private async Task AddItemsAsync(int first, int count)
    {
        for (var number = first; number < first + count; number++)
        {
            await bed.Library.Items.AddAsync(bed.NewItem(number), Token);
        }
    }

    /// <summary>
    /// Writes the WAL back into the database file, so the page overwritten next isn't shadowed by a newer copy in the
    /// WAL. Closing the library does it too, but not if a connection was still open then (MFC-1066).
    /// </summary>
    private async Task CheckpointAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        await connection.OpenAsync(Token);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await command.ExecuteReaderAsync(Token);
            if (await reader.ReadAsync(Token) && reader.GetInt32(0) == 0)
            {
                return;
            }

            await Task.Delay(100, Token);
        }
    }

    /// <summary>
    /// Closes the library, overwrites the root page of an index of the items with garbage, and opens it again. The
    /// header and the schema on page 1 stay intact, so the library still opens; the integrity check finds the damage.
    /// </summary>
    private async Task CorruptAsync()
    {
        await session.SetCurrentAsync(null);
        SqliteConnection.ClearAllPools();
        var databasePath = Path.Combine(directory, LibraryLayout.DatabaseFileName);
        await CheckpointAsync(databasePath);
        Assert.False(File.Exists(databasePath + "-wal") && new FileInfo(databasePath + "-wal").Length > 0, "The WAL wasn't checkpointed.");
        long pageSize;
        long rootPage;
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA page_size";
            pageSize = (long)(await command.ExecuteScalarAsync(Token))!;
            command.CommandText = "SELECT rootpage FROM sqlite_master WHERE name = 'IX_media_item_hashes_content_hash'";
            rootPage = (long)(await command.ExecuteScalarAsync(Token))!;
        }

        Assert.True(rootPage > 1);
        await using (var file = new FileStream(databasePath, FileMode.Open, FileAccess.Write))
        {
            file.Position = (rootPage - 1) * pageSize;
            await file.WriteAsync(Enumerable.Repeat((byte)0xFF, (int)pageSize).ToArray(), Token);
        }

        await bed.OpenAgainAsync();
        await session.SetCurrentAsync(bed.Library);
    }
}
