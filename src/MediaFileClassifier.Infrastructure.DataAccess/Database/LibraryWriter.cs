using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// The single writer of a library (D072). Writes are queued on a channel and run one at a time, in order, on one
/// connection, each in its own transaction. So the app never gets <c>SQLITE_BUSY</c> from its own writes, and a short
/// write such as a user edit waits at most for the write ahead of it, never for a whole bulk import.
/// Reads don't go through the writer; with WAL they run in parallel with it.
/// A write must not wait for another write of the same library: it would wait for itself. The writer rejects such a
/// write instead of deadlocking.
/// </summary>
internal sealed class LibraryWriter : IAsyncDisposable
{
    private static readonly AsyncLocal<LibraryWriter?> Running = new();

    private readonly SqliteConnection connection;
    private readonly Channel<Func<Task>> queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    private readonly Task loop;

    private LibraryWriter(SqliteConnection connection)
    {
        this.connection = connection;
        loop = Task.Run(RunAsync);
    }

    /// <summary>
    /// Opens the write connection and starts the writer.
    /// </summary>
    /// <param name="connections">Connection factory of the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The writer. The caller disposes it.</returns>
    public static async Task<LibraryWriter> StartAsync(LibraryConnectionFactory connections, CancellationToken cancellationToken)
    {
        var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new LibraryWriter(connection);
    }

    /// <summary>
    /// Queues a write and waits for it. It runs in a transaction that commits when <paramref name="work"/> returns and
    /// rolls back when it throws.
    /// </summary>
    /// <typeparam name="T">What the write returns.</typeparam>
    /// <param name="work">The write. It gets a tracking context; it calls <c>SaveChangesAsync</c> itself.</param>
    /// <param name="cancellationToken">Cancellation token. A queued write is dropped as soon as it's cancelled; a
    /// started write is rolled back if <paramref name="work"/> stops on the token, and commits otherwise.</param>
    /// <returns>What the write returned.</returns>
    public Task<T> ExecuteAsync<T>(Func<LibraryDbContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) =>
        Enqueue(work, transaction: true, cancellationToken);

    /// <summary>
    /// Queues a write and waits for it.
    /// </summary>
    /// <param name="work">The write.</param>
    /// <param name="cancellationToken">Cancellation token. A queued write is dropped as soon as it's cancelled; a
    /// started write is rolled back if <paramref name="work"/> stops on the token, and commits otherwise.</param>
    /// <returns>A task that completes when the write is committed.</returns>
    public Task ExecuteAsync(Func<LibraryDbContext, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        Enqueue(
            async (db, token) =>
            {
                await work(db, token).ConfigureAwait(false);
                return true;
            },
            transaction: true,
            cancellationToken);

    /// <summary>
    /// Queues work that can't run in a transaction, such as <c>VACUUM</c>, and waits for it.
    /// </summary>
    /// <param name="work">The work, given the write connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the work has run.</returns>
    public Task ExecuteWithoutTransactionAsync(Func<SqliteConnection, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        Enqueue(
            async (db, token) =>
            {
                await work(connection, token).ConfigureAwait(false);
                return true;
            },
            transaction: false,
            cancellationToken);

    /// <summary>
    /// Stops taking writes, waits for the queued ones, and closes the write connection.
    /// </summary>
    /// <returns>A task that completes when the writer has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        await loop.ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private Task<T> Enqueue<T>(Func<LibraryDbContext, CancellationToken, Task<T>> work, bool transaction, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Running.Value == this)
        {
            completion.SetException(new InvalidOperationException(
                "A library write can't wait for another write of the same library (D072); it would wait for itself."));
            return completion.Task;
        }

        // Cancelling a queued write completes the caller's task at once, so the caller doesn't wait for the writes
        // ahead of it. The registration is removed before the write starts, so a write that ran is never reported as
        // cancelled.
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var queued = queue.Writer.TryWrite(async () =>
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            if (completion.Task.IsCompleted || cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(await RunAsync(work, transaction, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        if (!queued)
        {
            registration.Dispose();
            completion.TrySetException(new ObjectDisposedException(nameof(LibraryWriter), "The library is closed."));
        }

        return completion.Task;
    }

    private async Task<T> RunAsync<T>(Func<LibraryDbContext, CancellationToken, Task<T>> work, bool transaction, CancellationToken cancellationToken)
    {
        await using var db = LibraryConnectionFactory.CreateContext(connection, ownsConnection: false, tracking: true);
        if (!transaction)
        {
            return await work(db, cancellationToken).ConfigureAwait(false);
        }

        await using var scope = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await work(db, cancellationToken).ConfigureAwait(false);
        await scope.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task RunAsync()
    {
        // Flows into every write, so a write that queues another write is caught in Enqueue.
        Running.Value = this;
        await foreach (var work in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            // Each work item reports its own result; nothing escapes to stop the loop.
            await work().ConfigureAwait(false);
        }
    }
}
