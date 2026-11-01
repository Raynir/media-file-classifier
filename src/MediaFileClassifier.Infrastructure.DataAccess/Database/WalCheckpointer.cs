using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// Copies the WAL into the database file in the background, on its own connection, so no write waits for it (MFC-982).
/// SQLite's automatic checkpoint runs inside the commit that fills the WAL past 1000 pages, and on a slow disk it held
/// that commit, and every write queued behind it, for up to 2 seconds. The writer turns it off and asks for a checkpoint
/// after each write instead. A passive checkpoint never blocks readers or the writer.
/// </summary>
internal sealed class WalCheckpointer : IAsyncDisposable
{
    /// <summary>
    /// The shortest time between two checkpoints. Requests in between are merged into one checkpoint, so the WAL holds
    /// at most about this much of the writes.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly SqliteConnection connection;
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly CancellationTokenSource stopping = new();
    private readonly Task loop;

    private WalCheckpointer(SqliteConnection connection)
    {
        this.connection = connection;
        loop = Task.Run(RunAsync);
    }

    /// <summary>
    /// Opens the checkpoint connection and starts the checkpointer.
    /// </summary>
    /// <param name="connections">Connection factory of the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checkpointer. The caller disposes it.</returns>
    public static async Task<WalCheckpointer> StartAsync(LibraryConnectionFactory connections, CancellationToken cancellationToken)
    {
        var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new WalCheckpointer(connection);
    }

    /// <summary>
    /// Asks for a checkpoint. It runs at once, or once the interval since the last one has passed.
    /// </summary>
    public void Request() => requests.Writer.TryWrite(true);

    /// <summary>
    /// Stops the checkpointer and closes its connection. Closing the library's last connection checkpoints the rest.
    /// </summary>
    /// <returns>A task that completes when the checkpointer has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        requests.Writer.TryComplete();
        await stopping.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
        stopping.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false))
            {
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    await command.ExecuteNonQueryAsync(stopping.Token).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // The next request tries again; the WAL keeps every commit until then.
                }

                await Task.Delay(Interval, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }
}
