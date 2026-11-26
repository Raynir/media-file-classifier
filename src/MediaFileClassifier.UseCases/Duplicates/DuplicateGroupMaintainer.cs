using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Duplicates;

/// <summary>
/// Keeps the open library's exact duplicate groups up to date (E12-S01-T02). The database queues the content hash of
/// every item that is added, deleted, gets a new hash (a full hash finished, or the file changed), or becomes Missing
/// or Excluded or comes back. After items change, this waits <see cref="DefaultDelay"/> so an import's batches are
/// taken together, then reconciles the queue in short writes of <see cref="BatchSize"/> hashes until it's empty. Each
/// write runs on the library's writer, between the import's own writes (D072). It also works off what's queued when
/// a library opens, such as the hashes an upgrade to schema 20 queued.
/// </summary>
/// <param name="session">The library session.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="delay">How long to wait after a change; <see cref="DefaultDelay"/> when <c>null</c>.</param>
public sealed partial class DuplicateGroupMaintainer(
    ILibrarySession session,
    TimeProvider clock,
    ILogger<DuplicateGroupMaintainer> logger,
    TimeSpan? delay = null) : IDisposable
{
    /// <summary>
    /// Content hashes reconciled per write, so a user's edit never waits long behind one.
    /// </summary>
    public const int BatchSize = 500;

    /// <summary>
    /// How long to wait after items changed before reconciling.
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private readonly Lock gate = new();
    private readonly TimeSpan wait = delay ?? DefaultDelay;
    private Attached? attached;
    private int started;

    /// <summary>
    /// Starts following the library session. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            session.CurrentChanged += OnCurrentChanged;
            Attach(session.Current);
        }
    }

    /// <summary>
    /// Waits until the open library's queue is worked off: for tests and the full detection job's callers.
    /// </summary>
    /// <returns>A task that completes when no reconciling is running or requested.</returns>
    public async Task IdleAsync()
    {
        while (true)
        {
            Task? running;
            lock (gate)
            {
                running = attached?.Running;
            }

            if (running is null)
            {
                return;
            }

            await running.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.CurrentChanged -= OnCurrentChanged;
        Attach(null);
    }

    private TimeProvider Clock => clock;

    private void OnCurrentChanged(object? sender, EventArgs e) => Attach(session.Current);

    private void Attach(IOpenLibrary? library)
    {
        Attached? old;
        lock (gate)
        {
            if (ReferenceEquals(attached?.Library, library))
            {
                return;
            }

            old = attached;
            attached = library is null ? null : new Attached(this, library);
        }

        old?.Dispose();
        lock (gate)
        {
            attached?.Start();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updating the duplicate groups failed; the next change of the items tries again")]
    private partial void LogFailed(Exception exception);

    /// <summary>
    /// The maintenance of one open library: at most one reconciling run at a time, and a change during a run makes it
    /// go round once more.
    /// </summary>
    private sealed class Attached(DuplicateGroupMaintainer owner, IOpenLibrary library) : IDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        private bool requested;

        public IOpenLibrary Library => library;

        public Task? Running { get; private set; }

        public void Start()
        {
            library.Items.Changed += OnItemsChanged;
            Request();
        }

        public void Dispose()
        {
            library.Items.Changed -= OnItemsChanged;

            // Under the lock, so no run starts with a disposed token; a run still going sees the cancellation.
            lock (owner.gate)
            {
                stopping.Cancel();
                stopping.Dispose();
            }
        }

        // Raised on the writer's thread: only flag the request; the run starts on the thread pool.
        private void OnItemsChanged(object? sender, EventArgs e) => Request();

        private void Request()
        {
            lock (owner.gate)
            {
                requested = true;
                if (Running is null && !stopping.IsCancellationRequested)
                {
                    var token = stopping.Token;
                    Running = Task.Run(() => RunAsync(token));
                }
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    lock (owner.gate)
                    {
                        if (!requested || token.IsCancellationRequested)
                        {
                            Running = null;
                            return;
                        }

                        requested = false;
                    }

                    await Task.Delay(owner.wait, owner.Clock, token).ConfigureAwait(false);
                    while ((await library.Duplicates.ReconcileAsync(BatchSize, owner.Clock.GetUtcNow(), token).ConfigureAwait(false)).Hashes > 0)
                    {
                    }
                }
            }
            catch (Exception exception) when ((exception is OperationCanceledException && token.IsCancellationRequested) || exception is ObjectDisposedException)
            {
                // The library closed or another one opened.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                owner.LogFailed(exception);
            }

            lock (owner.gate)
            {
                Running = null;
            }
        }
    }
}
