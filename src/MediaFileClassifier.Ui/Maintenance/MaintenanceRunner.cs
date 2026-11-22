using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Maintenance;

/// <summary>
/// Runs one library maintenance task at a time in the background (E02-S06-T01): Refresh search index or Check library
/// integrity. The status bar shows it through <see cref="Changed"/>, and <see cref="Cancel"/> stops it. Closing the
/// library stops it too. Thumbnails are a job, not a task of this runner.
/// </summary>
/// <param name="session">Holds the open library, or <c>null</c> where there's none.</param>
/// <param name="logger">Logger, or <c>null</c> for none.</param>
public sealed partial class MaintenanceRunner(ILibrarySession? session = null, ILogger<MaintenanceRunner>? logger = null)
{
    private readonly ILogger logger = logger ?? NullLogger<MaintenanceRunner>.Instance;

    private readonly Lock gate = new();
    // Stops the running task; the task owns and disposes its cancellation source.
    private Action? cancelRunning;
    private bool watchesSession;

    /// <summary>
    /// Raised when a task starts, reports progress or ends. It may be raised on any thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the running task, or <c>null</c>.
    /// </summary>
    public MaintenanceTask? Current { get; private set; }

    /// <summary>
    /// Gets the share of the running task done, from 0 to 1, or <c>null</c> when it doesn't report one.
    /// </summary>
    public double? Progress { get; private set; }

    /// <summary>
    /// Runs a task unless another one is running.
    /// </summary>
    /// <typeparam name="T">The task's result.</typeparam>
    /// <param name="task">Which task it is.</param>
    /// <param name="work">The work; it reports progress from 0 to 1 and stops when the token is cancelled.</param>
    /// <returns>How it ended, and its result when it finished.</returns>
    public async Task<MaintenanceRun<T>> RunAsync<T>(MaintenanceTask task, Func<IProgress<double>, CancellationToken, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        CancellationTokenSource cancellation;
        lock (gate)
        {
            if (cancelRunning is not null)
            {
                return new(MaintenanceRunState.Busy, default);
            }

            WatchSession();
            cancellation = new CancellationTokenSource();
            cancelRunning = cancellation.Cancel;
            Current = task;
            Progress = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var progress = new RunProgress(this, cancellation);

            // Off the UI thread: the services run parts of their work synchronously.
            var result = await Task.Run(() => work(progress, cancellation.Token), cancellation.Token);
            return new(MaintenanceRunState.Finished, result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            LogCancelled(task);
            return new(MaintenanceRunState.Cancelled, default);
        }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
        {
            // The library closed under the task.
            LogCancelled(task);
            return new(MaintenanceRunState.Cancelled, default);
        }
        finally
        {
            lock (gate)
            {
                cancelRunning = null;
                Current = null;
                Progress = null;
            }

            cancellation.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stops the running task, if any.
    /// </summary>
    public void Cancel()
    {
        lock (gate)
        {
            cancelRunning?.Invoke();
        }
    }

    private void WatchSession()
    {
        if (!watchesSession && session is not null)
        {
            watchesSession = true;
            session.CurrentChanged += (_, _) => Cancel();
        }
    }

    private void Report(CancellationTokenSource source, double value)
    {
        lock (gate)
        {
            if (source.IsCancellationRequested || cancelRunning?.Target != source)
            {
                return;
            }

            Progress = Math.Clamp(value, 0, 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Library task {Task} was stopped")]
    private partial void LogCancelled(MaintenanceTask task);

    // Reports synchronously on the worker's thread, so the status bar marshals to the UI thread itself.
    private sealed class RunProgress(MaintenanceRunner runner, CancellationTokenSource source) : IProgress<double>
    {
        public void Report(double value) => runner.Report(source, value);
    }
}

/// <summary>
/// A library maintenance task that <see cref="MaintenanceRunner"/> runs.
/// </summary>
public enum MaintenanceTask
{
    /// <summary>
    /// Refresh search index.
    /// </summary>
    RefreshIndex,

    /// <summary>
    /// Check library integrity.
    /// </summary>
    CheckIntegrity,

    /// <summary>
    /// Compact the library database. It can't be stopped once it has started.
    /// </summary>
    CompactDatabase,
}

/// <summary>
/// How a maintenance task ended.
/// </summary>
public enum MaintenanceRunState
{
    /// <summary>
    /// It finished.
    /// </summary>
    Finished,

    /// <summary>
    /// It was stopped, by the user or because the library closed.
    /// </summary>
    Cancelled,

    /// <summary>
    /// It didn't start, because another task was running.
    /// </summary>
    Busy,
}

/// <summary>
/// How a maintenance task ended, and its result.
/// </summary>
/// <typeparam name="T">The result.</typeparam>
/// <param name="State">How it ended.</param>
/// <param name="Result">The result, when it finished.</param>
public sealed record MaintenanceRun<T>(MaintenanceRunState State, T? Result);
