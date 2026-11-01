using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// Checks SQLite on the device through the real data access code (MFC-18): create, migrate, write, reopen and
/// back up a library, then check the pragmas and FTS5 on a raw connection.
/// </summary>
/// <param name="libraries">The library store.</param>
/// <param name="paths">App folders.</param>
/// <param name="time">Clock.</param>
internal sealed class SqliteCheck(ILibraryStore libraries, IAppPaths paths, TimeProvider time)
{
    /// <summary>
    /// Runs the check.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task RunAsync()
    {
        var parent = Path.Combine(paths.DataDirectory, "spike");
        Directory.CreateDirectory(parent);
        var name = "smoke-" + time.GetUtcNow().ToUnixTimeMilliseconds();

        var stopwatch = Stopwatch.StartNew();
        string libraryDirectory;
        await using (var library = await libraries.CreateAsync(parent, name))
        {
            SpikeLog.Write($"sqlite: CreateAsync (EF model, migration) took {stopwatch.ElapsedMilliseconds} ms");
            libraryDirectory = library.Layout.RootDirectory;

            stopwatch.Restart();
            var now = time.GetUtcNow();
            var folder = new IndexedFolder(
                Id<IndexedFolder>.New(),
                library.Library.Id,
                "/storage/emulated/0/DCIM",
                "content://spike",
                new FolderRules(),
                FolderStatus.Online,
                now,
                now);
            await library.Folders.AddAsync(folder);
            var folders = await library.Folders.GetAllAsync();
            SpikeLog.Write($"sqlite: write and read back {folders.Count} folder(s) in {stopwatch.ElapsedMilliseconds} ms");

            stopwatch.Restart();
            var integrity = await library.Maintenance.CheckIntegrityAsync();
            SpikeLog.Write($"sqlite: integrity {(integrity.IsOk ? "ok" : string.Join("; ", integrity.Problems))} in {stopwatch.ElapsedMilliseconds} ms");

            stopwatch.Restart();
            await library.Maintenance.BackupAsync(Path.Combine(parent, name + "-backup.db"));
            SpikeLog.Write($"sqlite: VACUUM INTO backup in {stopwatch.ElapsedMilliseconds} ms");
        }

        stopwatch.Restart();
        await using (var reopened = await libraries.OpenAsync(libraryDirectory))
        {
            SpikeLog.Write($"sqlite: OpenAsync (warm) took {stopwatch.ElapsedMilliseconds} ms, {(await reopened.Folders.GetAllAsync()).Count} folder(s)");
        }

        // A second library in the same process: EF Core's model is built by now, so this is the per-library cost.
        stopwatch.Restart();
        await using (await libraries.CreateAsync(parent, name + "-second"))
        {
            SpikeLog.Write($"sqlite: second CreateAsync (EF model warm) took {stopwatch.ElapsedMilliseconds} ms");
        }

        CheckRawConnection(Path.Combine(libraryDirectory, LibraryLayout.DatabaseFileName));
        CheckFts5(Path.Combine(parent, name + "-fts.db"));
        SpikeLog.Write("sqlite: " + SpikeLog.Memory());
    }

    private static void CheckRawConnection(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        SpikeLog.Write($"sqlite: version {Scalar(connection, "SELECT sqlite_version()")}, "
            + $"journal_mode {Scalar(connection, "PRAGMA journal_mode")}, "
            + $"schema_version rows {Scalar(connection, "SELECT count(*) FROM schema_version")}");

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA compile_options";
        using var reader = command.ExecuteReader();
        var options = new List<string>();
        while (reader.Read())
        {
            options.Add(reader.GetString(0));
        }

        SpikeLog.Write("sqlite: compile options with FTS: " + string.Join(", ", options.Where(option => option.Contains("FTS", StringComparison.Ordinal))));
    }

    private static void CheckFts5(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, "CREATE VIRTUAL TABLE notes USING fts5(body, tokenize = 'unicode61 remove_diacritics 2')");
        Execute(connection, "CREATE VIRTUAL TABLE notes_trigram USING fts5(body, tokenize = 'trigram')");
        string[] rows = ["A dog playing on the beach", "Собака играет на пляже", "Café crème in Zürich", "Quarterly spreadsheet"];
        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO notes(body) VALUES ($body); INSERT INTO notes_trigram(body) VALUES ($body)";
            insert.Parameters.AddWithValue("$body", row);
            insert.ExecuteNonQuery();
        }

        SpikeLog.Write($"sqlite: fts5 'beach' -> {Scalar(connection, "SELECT body FROM notes WHERE notes MATCH 'beach'")}; "
            + $"'пляже' -> {Scalar(connection, "SELECT body FROM notes WHERE notes MATCH 'пляже'")}; "
            + $"'cafe' -> {Scalar(connection, "SELECT body FROM notes WHERE notes MATCH 'cafe'")}; "
            + $"trigram 'adsh' -> {Scalar(connection, "SELECT body FROM notes_trigram WHERE notes_trigram MATCH 'adsh'")}");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "The checks pass constant SQL.")]
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "The checks pass constant SQL.")]
    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
