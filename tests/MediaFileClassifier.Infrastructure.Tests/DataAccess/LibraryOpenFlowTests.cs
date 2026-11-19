using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.UseCases.Search.Filters;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for opening libraries in the app (E02-S02): stale locks, failed migrations and their restore, and library
/// settings stored in the open library.
/// </summary>
public sealed class LibraryOpenFlowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenAsync_LockFileLeftByACrash_DoesNotLockTheLibrary()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        await bed.Library.DisposeAsync();

        // A crashed instance leaves its lock file behind, but the operating system has released the handle (D077).
        File.WriteAllText(layout.LockFilePath, "pid=999999 machine=CRASHED");
        await bed.OpenAgainAsync();

        Assert.Equal(layout.RootDirectory, bed.Library.Layout.RootDirectory);
    }

    [Fact]
    public async Task OpenAsync_FailedMigration_ReportsTheBackupWhichRestores()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var directory = bed.Library.Layout.RootDirectory;
        await bed.Library.DisposeAsync();
        BreakLatestMigration(bed.Temporary.DatabasePath);
        var stages = new List<LibraryOpenStage>();

        var failure = await Assert.ThrowsAsync<LibraryOpenException>(
            () => bed.Store.OpenAsync(directory, new SyncProgress(stages.Add), Token));

        Assert.Equal(LibraryOpenError.MigrationFailed, failure.Error);
        Assert.Equal([LibraryOpenStage.Migrating], stages);
        Assert.True(File.Exists(failure.BackupPath));

        SqliteConnection.ClearAllPools();
        await bed.Store.RestoreBackupAsync(directory, failure.BackupPath!, Token);

        Assert.Equal(File.ReadAllBytes(failure.BackupPath!), File.ReadAllBytes(bed.Temporary.DatabasePath));
        Assert.False(File.Exists(bed.Temporary.DatabasePath + "-wal"));

        // The lock is free again: the same failure, not "in use". A later time gives the new backup another name.
        bed.Clock.Tick();
        SqliteConnection.ClearAllPools();
        var again = await Assert.ThrowsAsync<LibraryOpenException>(() => bed.Store.OpenAsync(directory, Token));
        Assert.Equal(LibraryOpenError.MigrationFailed, again.Error);
    }

    [Fact]
    public async Task LibrarySettings_AreStoredInTheOpenLibrary()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var session = new LibrarySession();
        var values = new OpenLibrarySettingValues(session);
        var availability = 0;
        values.AvailabilityChanged += (_, _) => availability++;
        Assert.False(values.IsAvailable);

        await session.SetCurrentAsync(bed.Library);
        values.Apply(new Dictionary<string, System.Text.Json.JsonElement?>
        {
            [KnownSettings.RetainedPreviousRuns.Key] = KnownSettings.RetainedPreviousRuns.Serialize(4),
            [KnownSettings.AnalyzeAutomatically.Key] = KnownSettings.AnalyzeAutomatically.Serialize(false),
        });
        await session.SetCurrentAsync(null);
        await bed.OpenAgainAsync();

        Assert.Equal(2, availability);
        Assert.Equal(4, bed.Library.Library.Settings.RetainedPreviousRuns);
        Assert.False(bed.Library.Library.Settings.AnalyzeAutomatically);

        await session.SetCurrentAsync(bed.Library);
        Assert.True(values.TryGet(KnownSettings.RetainedPreviousRuns.Key, out var stored));
        Assert.Equal(4, stored.GetInt32());

        values.Apply(new Dictionary<string, System.Text.Json.JsonElement?> { [KnownSettings.RetainedPreviousRuns.Key] = null });
        Assert.True(values.TryGet(KnownSettings.RetainedPreviousRuns.Key, out var reset));
        Assert.Equal(1, reset.GetInt32());
        Assert.Throws<ArgumentException>(() => values.Apply(new Dictionary<string, System.Text.Json.JsonElement?> { ["appearance.theme"] = null }));
        await session.SetCurrentAsync(null);
    }

    [Fact]
    public async Task LastFilters_AreStoredInTheOpenLibrary()
    {
        // MFC-1001: saving the filters threw "search.lastFilters isn't a library setting", which failed imports.
        const string Filters = """{"version":1,"filters":[]}""";
        await using var bed = await LibraryTestBed.CreateAsync();
        var session = new LibrarySession();
        var values = new OpenLibrarySettingValues(session);
        await session.SetCurrentAsync(bed.Library);

        values.Apply(new Dictionary<string, System.Text.Json.JsonElement?> { [FilterSettings.LastFilters.Key] = FilterSettings.LastFilters.Serialize(Filters) });
        await session.SetCurrentAsync(null);
        await bed.OpenAgainAsync();
        await session.SetCurrentAsync(bed.Library);

        Assert.Equal(Filters, bed.Library.Library.Settings.LastFilters);
        Assert.True(values.TryGet(FilterSettings.LastFilters.Key, out var stored));
        Assert.Equal(Filters, stored.GetString());
        await session.SetCurrentAsync(null);
    }

    [Fact]
    public async Task EveryLibrarySetting_CanBeStoredInTheOpenLibrary()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var session = new LibrarySession();
        var values = new OpenLibrarySettingValues(session);
        await session.SetCurrentAsync(bed.Library);
        var settings = new[] { typeof(KnownSettings).Assembly, typeof(FilterSettings).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(field => typeof(SettingDefinition).IsAssignableFrom(field.FieldType))
            .Select(field => (SettingDefinition)field.GetValue(null)!)
            .Where(setting => setting.Scope == SettingScope.Library)
            .ToList();

        Assert.Contains(FilterSettings.LastFilters, settings);
        foreach (var setting in settings)
        {
            values.Apply(new Dictionary<string, System.Text.Json.JsonElement?> { [setting.Key] = setting.Serialize(setting.DefaultValue) });
            Assert.True(values.TryGet(setting.Key, out _), setting.Key);
        }

        await session.SetCurrentAsync(null);
    }

    [Fact]
    public async Task LibrarySettings_FailedWrite_KeepsTheStoredValue()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var session = new LibrarySession();
        var values = new OpenLibrarySettingValues(session);
        await session.SetCurrentAsync(bed.Library);

        // A closed library can't be written to.
        await bed.Library.DisposeAsync();

        Assert.Throws<IOException>(() => values.Apply(new Dictionary<string, System.Text.Json.JsonElement?>
        {
            [KnownSettings.RetainedPreviousRuns.Key] = KnownSettings.RetainedPreviousRuns.Serialize(4),
        }));
        Assert.True(values.TryGet(KnownSettings.RetainedPreviousRuns.Key, out var stored));
        Assert.Equal(1, stored.GetInt32());
    }

    // Drops the latest migration from the history but keeps its changes, so applying it again fails.
    private static void BreakLatestMigration(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        // The two latest: the latest (RussianStems) recreates its table and runs again cleanly, while the one before it
        // creates a table that already exists and fails.
        command.CommandText = "DELETE FROM schema_version WHERE MigrationId IN (SELECT MigrationId FROM schema_version ORDER BY MigrationId DESC LIMIT 2)";
        command.ExecuteNonQuery();
    }

    private sealed class SyncProgress(Action<LibraryOpenStage> report) : IProgress<LibraryOpenStage>
    {
        public void Report(LibraryOpenStage value) => report(value);
    }
}
