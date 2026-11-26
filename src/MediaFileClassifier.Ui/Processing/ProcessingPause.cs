using Avalonia.Threading;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// Pausing and resuming all processing and single pools from the UI (E05-S05-T02), shared by the processing panel and
/// the status bar so both show the same state. It follows the open library's scheduler, whoever pauses it.
/// </summary>
public sealed class ProcessingPause : IDisposable
{
    private readonly IJobSystem? jobs;
    private readonly ProgressFeed feed;
    private readonly Action<Action> post;
    private IJobScheduler? followed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessingPause"/> class.
    /// </summary>
    /// <param name="feed">The job progress; each update also checks which library's scheduler to follow.</param>
    /// <param name="jobs">The job system, or <see langword="null"/> for none: nothing is ever paused then.</param>
    /// <param name="post">Runs an action on the UI thread; <see cref="Dispatcher.UIThread"/> by default.</param>
    public ProcessingPause(ProgressFeed feed, IJobSystem? jobs = null, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(feed);

        this.feed = feed;
        this.jobs = jobs;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        feed.Updated += OnProgress;
        Follow();
    }

    /// <summary>
    /// Raised on the UI thread after anything paused or resumed: all processing, a pool or a job.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether all processing is paused.
    /// </summary>
    public bool IsPaused => jobs?.Current?.IsPaused == true;

    /// <summary>
    /// Gets the pools paused on their own, in pool order.
    /// </summary>
    public IReadOnlyList<ResourcePool> PausedPools =>
        jobs?.Current is { } scheduler ? [.. Enum.GetValues<ResourcePool>().Where(scheduler.IsPoolPaused)] : [];

    /// <summary>
    /// Gets a value indicating whether all processing or any pool is paused.
    /// </summary>
    public bool IsAnythingPaused => IsPaused || PausedPools.Count > 0;

    /// <summary>
    /// Pauses all processing.
    /// </summary>
    public void Pause()
    {
        Follow();
        jobs?.Current?.Pause();
    }

    /// <summary>
    /// Resumes all processing and every paused pool. Jobs paused one by one stay paused.
    /// </summary>
    public void ResumeAll()
    {
        Follow();
        if (jobs?.Current is not { } scheduler)
        {
            return;
        }

        foreach (var pool in Enum.GetValues<ResourcePool>())
        {
            scheduler.ResumePool(pool);
        }

        scheduler.Resume();
    }

    /// <summary>
    /// Pauses all processing if it's running, or resumes everything if anything is paused.
    /// </summary>
    public void Toggle()
    {
        if (IsAnythingPaused)
        {
            ResumeAll();
        }
        else
        {
            Pause();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a pool is paused on its own.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <returns><see langword="true"/> if it's paused.</returns>
    public bool IsPoolPaused(ResourcePool pool) => jobs?.Current?.IsPoolPaused(pool) == true;

    /// <summary>
    /// Pauses or resumes one pool.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="paused">Whether it should be paused.</param>
    public void SetPoolPaused(ResourcePool pool, bool paused)
    {
        Follow();
        if (jobs?.Current is not { } scheduler)
        {
            return;
        }

        if (paused)
        {
            scheduler.PausePool(pool);
        }
        else
        {
            scheduler.ResumePool(pool);
        }
    }

    /// <summary>
    /// Stops following the scheduler.
    /// </summary>
    public void Dispose()
    {
        feed.Updated -= OnProgress;
        if (followed is not null)
        {
            followed.PauseChanged -= OnPauseChanged;
            followed = null;
        }
    }

    // The progress is reset when a library opens or closes, so an update is when the scheduler may have changed.
    private void OnProgress(object? sender, ProgressSummary summary)
    {
        if (Follow())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Follows the current scheduler's pause changes. Called on the UI thread.
    /// </summary>
    /// <returns><see langword="true"/> if the scheduler changed.</returns>
    private bool Follow()
    {
        var current = jobs?.Current;
        if (ReferenceEquals(current, followed))
        {
            return false;
        }

        if (followed is not null)
        {
            followed.PauseChanged -= OnPauseChanged;
        }

        followed = current;
        if (current is not null)
        {
            current.PauseChanged += OnPauseChanged;
        }

        return true;
    }

    private void OnPauseChanged(object? sender, EventArgs e) => post(() => Changed?.Invoke(this, EventArgs.Empty));
}
