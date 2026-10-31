using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// Opens connections to one library database with the pragmas every connection needs (E01-S03-T03):
/// <list type="bullet">
/// <item><c>journal_mode=WAL</c>: readers never wait for the writer, and the writer never waits for readers.</item>
/// <item><c>synchronous=NORMAL</c>: safe with WAL; a power cut can lose the last commits but never corrupts.</item>
/// <item><c>foreign_keys=ON</c>: SQLite leaves foreign keys off by default.</item>
/// <item><c>busy_timeout</c>: a backstop; the app's own writes never collide, because they run one at a time (D072).</item>
/// </list>
/// FTS5 is compiled into the bundled SQLite (<c>SQLitePCLRaw.bundle_e_sqlite3</c>); no extension is loaded (D067).
/// </summary>
/// <param name="databasePath">Full path of the database file.</param>
internal sealed class LibraryConnectionFactory(string databasePath)
{
    /// <summary>
    /// How long a connection waits for a lock before failing with <c>SQLITE_BUSY</c>.
    /// </summary>
    public const int BusyTimeoutMilliseconds = 5000;

    private const string Pragmas =
        "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    public string ConnectionString { get; } = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true,
        ForeignKeys = true,
    }.ToString();

    /// <summary>
    /// Opens a connection and applies the pragmas.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open connection. The caller disposes it.</returns>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = Pragmas;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens a connection and creates a context on it, which closes the connection when it's disposed.
    /// </summary>
    /// <param name="tracking">Whether the context tracks loaded entities. Reads don't.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The context. The caller disposes it.</returns>
    public async Task<LibraryDbContext> CreateContextAsync(bool tracking = false, CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return CreateContext(connection, ownsConnection: true, tracking);
    }

    /// <summary>
    /// Runs a read on its own no-tracking context, on a thread-pool thread. Microsoft.Data.Sqlite runs its async
    /// methods synchronously, so without the hop a read called from the UI thread would block it.
    /// </summary>
    /// <typeparam name="T">What the read returns.</typeparam>
    /// <param name="read">The read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the read returned.</returns>
    public Task<T> ReadAsync<T>(Func<LibraryDbContext, CancellationToken, Task<T>> read, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                await using var db = await CreateContextAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return await read(db, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Creates a context on an open connection.
    /// </summary>
    /// <param name="connection">The connection, from <see cref="OpenAsync"/>.</param>
    /// <param name="ownsConnection">Whether disposing the context closes the connection.</param>
    /// <param name="tracking">Whether the context tracks loaded entities.</param>
    /// <returns>The context. The caller disposes it.</returns>
    public static LibraryDbContext CreateContext(SqliteConnection connection, bool ownsConnection, bool tracking)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(connection, ownsConnection, sqlite => sqlite.MigrationsHistoryTable(LibraryDbContext.SchemaVersionTable))
            .UseQueryTrackingBehavior(tracking ? QueryTrackingBehavior.TrackAll : QueryTrackingBehavior.NoTracking)
            .Options;
        return new LibraryDbContext(options);
    }
}
