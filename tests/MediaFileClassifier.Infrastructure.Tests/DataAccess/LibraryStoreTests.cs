using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using MediaFileClassifier.Testing.Libraries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryStore"/>: the folder layout, the lock file and migrate-on-open (E01-S03-T01, T05).
/// </summary>
public sealed class LibraryStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_MakesFolderWithDatabaseThumbnailsAndLock()
    {
        using var temporary = TemporaryLibrary.Reserve("Holidays");
        var store = LibraryTestBed.CreateStore(new TestClock(LibraryTestBed.Start));

        await using (var library = await store.CreateAsync(temporary.ParentDirectory, "Holidays", Token))
        {
            Assert.Equal(temporary.LibraryDirectory, library.Layout.RootDirectory);
            Assert.True(File.Exists(library.Layout.DatabasePath));
            Assert.True(Directory.Exists(library.Layout.ThumbnailsDirectory));
            Assert.True(File.Exists(library.Layout.LockFilePath));
            Assert.Equal("Holidays", library.Library.Name);
            Assert.Equal(LibraryTestBed.Start, library.Library.CreatedAt);
        }

        // Closing checkpoints the WAL into the database file.
        Assert.False(File.Exists(temporary.DatabasePath + "-wal"));
    }

    [Fact]
    public async Task CreateAsync_NewLibraryHasLatestSchema()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        await using var db = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(cancellationToken: Token);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Token));
        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
    }

    [Fact]
    public void SchemaVersion_IsTheNumberOfMigrations()
    {
        // Each migration is one schema version; adding a migration means raising Library.CurrentSchemaVersion.
        using var db = new DesignTimeLibraryDbContextFactory().CreateDbContext([]);

        var migrationCount = db.Database.GetMigrations().Count();
        Assert.Equal(Library.CurrentSchemaVersion, migrationCount);
    }

    [Fact]
    public async Task CreateAsync_FolderExistsAndIsNotEmpty_ThrowsAlreadyExists()
    {
        using var temporary = TemporaryLibrary.Create();
        var store = LibraryTestBed.CreateStore(TimeProvider.System);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => store.CreateAsync(temporary.ParentDirectory, "test", Token));

        Assert.Equal(LibraryOpenError.AlreadyExists, exception.Error);
    }

    [Fact]
    public async Task CreateAsync_FailsAfterDatabaseIsWritten_RemovesFolderSoItCanBeCreatedAgain()
    {
        using var temporary = TemporaryLibrary.Reserve();
        var failing = new LibraryStore(new TestClock(LibraryTestBed.Start), new ThrowingLogger<LibraryStore>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateAsync(temporary.ParentDirectory, "test", Token));

        Assert.False(Directory.Exists(temporary.LibraryDirectory));
        await using var library = await LibraryTestBed.CreateStore(TimeProvider.System).CreateAsync(temporary.ParentDirectory, "test", Token);
        Assert.Equal("test", library.Library.Name);
    }

    [Fact]
    public async Task CreateAsync_FailsInEmptyFolderGivenByUser_KeepsFolderAndEmptiesIt()
    {
        using var temporary = TemporaryLibrary.Reserve();
        Directory.CreateDirectory(temporary.LibraryDirectory);
        var failing = new LibraryStore(new TestClock(LibraryTestBed.Start), new ThrowingLogger<LibraryStore>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateAsync(temporary.ParentDirectory, "test", Token));

        Assert.True(Directory.Exists(temporary.LibraryDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.LibraryDirectory));
    }

    [Fact]
    public async Task OpenAsync_LibraryAlreadyOpen_ThrowsInUse()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => bed.Store.OpenAsync(bed.Temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.InUse, exception.Error);
    }

    [Fact]
    public async Task OpenAsync_LockFileNotWritable_ThrowsNotWritable()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();
        var lockFile = new FileInfo(bed.Library.Layout.LockFilePath);
        lockFile.IsReadOnly = true;
        try
        {
            var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => bed.Store.OpenAsync(bed.Temporary.LibraryDirectory, Token));

            Assert.Equal(LibraryOpenError.NotWritable, exception.Error);
        }
        finally
        {
            lockFile.IsReadOnly = false;
        }

        await bed.OpenAgainAsync();
    }

    [Fact]
    public async Task OpenAsync_AfterClose_ReleasesLockAndKeepsData()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        await bed.ReopenAsync();

        Assert.NotNull(await bed.Library.Items.GetAsync(item.Id, Token));
        Assert.Equal("test", bed.Library.Library.Name);
    }

    [Fact]
    public async Task DisposeAsync_ThenReadOrWrite_ThrowsObjectDisposed()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => bed.Library.Items.CountAsync(Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => bed.Library.Maintenance.CheckIntegrityAsync(Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => bed.Library.Items.AddAsync(bed.NewItem(1), Token));
    }

    [Fact]
    public async Task OpenAsync_MissingLibrary_ThrowsNotFound()
    {
        using var temporary = TemporaryLibrary.Reserve();
        var store = LibraryTestBed.CreateStore(TimeProvider.System);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => store.OpenAsync(temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.NotFound, exception.Error);
    }

    [Fact]
    public async Task OpenAsync_FileIsNotSqlite_ThrowsNotALibrary()
    {
        using var temporary = TemporaryLibrary.Reserve();
        Directory.CreateDirectory(temporary.LibraryDirectory);
        const string Text = "This is not a database, but it is long enough to have a header.";
        await File.WriteAllTextAsync(temporary.DatabasePath, Text, Token);
        var store = LibraryTestBed.CreateStore(TimeProvider.System);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => store.OpenAsync(temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.NotALibrary, exception.Error);
        Assert.Equal(Text, await File.ReadAllTextAsync(temporary.DatabasePath, Token));
        Assert.False(File.Exists(Path.Combine(temporary.LibraryDirectory, LibraryLayout.LockFileName)));
    }

    [Fact]
    public async Task OpenAsync_EmptyDatabase_ThrowsNotALibraryAndLeavesItUntouched()
    {
        using var temporary = TemporaryLibrary.Create();
        var store = LibraryTestBed.CreateStore(TimeProvider.System);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => store.OpenAsync(temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.NotALibrary, exception.Error);
        Assert.Equal(0, new FileInfo(temporary.DatabasePath).Length);
        Assert.False(File.Exists(Path.Combine(temporary.LibraryDirectory, LibraryLayout.LockFileName)));
        using var connection = temporary.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'media_item'";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task OpenAsync_SchemaFromNewerApp_ThrowsNewerVersion()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();
        using (var connection = bed.Temporary.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO schema_version (MigrationId, ProductVersion) VALUES ('99990101000000_FromTheFuture', '99.0')";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => bed.Store.OpenAsync(bed.Temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.NewerVersion, exception.Error);
    }

    [Fact]
    public async Task OpenAsync_StaleLibrarySchemaVersion_IsRepaired()
    {
        // As if the app stopped between migrating and updating library.schema_version.
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();
        using (var connection = bed.Temporary.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE library SET schema_version = 0";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        await bed.OpenAgainAsync();

        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
    }

    [Fact]
    public async Task OpenAsync_LibraryAtEachOlderVersion_BacksUpAndUpgradesKeepingData()
    {
        // Runs for every older schema version (N-1 = 0 has no library record, so it starts at 1). After the
        // upgrade the columns must match a new library's, and the item must keep its values and save new ones.
        using var probe = new DesignTimeLibraryDbContextFactory().CreateDbContext([]);
        var migrations = probe.Database.GetMigrations().ToList();

        for (var version = 1; version < migrations.Count; version++)
        {
            await using var bed = await LibraryTestBed.CreateAsync();
            var item = bed.NewItem(1);
            item.SetFavorite(true, bed.Clock.Tick());
            item.MarkReviewed(bed.Clock.Tick());
            await bed.Library.Items.AddAsync(item, Token);
            await bed.Library.DisposeAsync();
            var newColumns = ReadColumns(bed.Temporary);
            await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
            {
                await db.GetService<IMigrator>().MigrateAsync(migrations[version - 1], cancellationToken: Token);
            }

            SqliteConnection.ClearAllPools();

            await bed.OpenAgainAsync();

            Assert.Single(Directory.GetFiles(bed.Library.Layout.BackupsDirectory, $"library-v{version}-*.db"));
            Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
            Assert.Equal(newColumns, ReadColumns(bed.Temporary));

            var upgraded = await bed.Library.Items.GetAsync(item.Id, Token);
            Assert.NotNull(upgraded);
            Assert.True(upgraded.IsFavorite);
            Assert.Equal(ReviewStatus.Reviewed, upgraded.ReviewStatus);

            upgraded.SetFavorite(false, bed.Clock.Tick());
            upgraded.MarkUnreviewed(bed.Clock.Tick());
            await bed.Library.Items.UpdateAsync(upgraded, Token);
            Assert.Equivalent(upgraded, await bed.Library.Items.GetAsync(item.Id, Token));
        }
    }

    [Fact]
    public void AddDataAccess_RegistersLibraryStore()
    {
        using var provider = new ServiceCollection().AddLogging().AddDataAccess().BuildServiceProvider();

        Assert.IsType<LibraryStore>(provider.GetRequiredService<ILibraryStore>());
    }

    private static List<string> ReadColumns(TemporaryLibrary temporary)
    {
        using var connection = temporary.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.name || '.' || c.name || ' ' || c.type || iif(c."notnull", ' NOT NULL', '')
            FROM sqlite_master AS m, pragma_table_info(m.name) AS c
            WHERE m.type = 'table'
            ORDER BY 1
            """;
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
