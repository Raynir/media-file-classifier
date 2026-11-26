namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Pauses folder monitoring for the whole app (E04-S06-T02). While it's paused the watchers keep running and keep
/// gathering what changes, but nothing is queued for import, and rescans that monitoring would start (after lost events
/// or a drive coming back) wait. Resuming releases what was held. The pause isn't stored: a restart monitors again, and
/// the startup sync finds what changed meanwhile. It's separate from pausing all processing (E05-S05), which holds the
/// jobs monitoring already queued (D536).
/// </summary>
public sealed class MonitoringPause
{
    private readonly Lock syncRoot = new();
    private TaskCompletionSource resumed = CreateResumed();
    private bool paused;

    /// <summary>
    /// Raised after monitoring was paused or resumed, on the thread that did it.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether monitoring is paused.
    /// </summary>
    public bool IsPaused
    {
        get
        {
            lock (syncRoot)
            {
                return paused;
            }
        }
    }

    /// <summary>
    /// Pauses monitoring. Does nothing when it's paused.
    /// </summary>
    public void Pause() => Set(true);

    /// <summary>
    /// Resumes monitoring. Does nothing when it isn't paused.
    /// </summary>
    public void Resume() => Set(false);

    /// <summary>
    /// Waits while monitoring is paused.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes at once when monitoring isn't paused, or when it's resumed.</returns>
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (syncRoot)
        {
            wait = paused ? resumed.Task : Task.CompletedTask;
        }

        return wait.IsCompleted ? wait : wait.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateResumed() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Set(bool value)
    {
        TaskCompletionSource? release = null;
        lock (syncRoot)
        {
            if (paused == value)
            {
                return;
            }

            paused = value;
            if (value)
            {
                resumed = CreateResumed();
            }
            else
            {
                release = resumed;
            }
        }

        release?.TrySetResult();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
