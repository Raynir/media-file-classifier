using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// Brings the job progress to the UI thread (E05-S02-T02), at most once per <see cref="Interval"/> however fast work
/// items finish. Changes in between are coalesced: each update is a fresh snapshot of every job. While jobs are
/// active it also updates once per interval, so the estimate keeps moving when no item finishes for a while.
/// </summary>
public sealed class ProgressFeed : IDisposable
{
    /// <summary>
    /// The shortest time between two updates.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly IJobProgress progress;
    private readonly Action<Action> post;
    private readonly ITimer timer;
    private readonly Lock gate = new();
    private bool coolingDown;
    private bool changed;
    private volatile bool active;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressFeed"/> class.
    /// </summary>
    /// <param name="progress">The job progress.</param>
    /// <param name="clock">Clock, for the interval timer.</param>
    /// <param name="post">Runs an action on the UI thread; <see cref="Dispatcher.UIThread"/> by default.</param>
    public ProgressFeed(IJobProgress progress, TimeProvider clock, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.progress = progress;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        timer = clock.CreateTimer(_ => OnInterval(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        progress.Changed += OnChanged;
    }

    /// <summary>
    /// Raised on the UI thread with the new progress.
    /// </summary>
    public event EventHandler<ProgressSummary>? Updated;

    /// <summary>
    /// Gets the progress of the last update. Read it on the UI thread.
    /// </summary>
    public ProgressSummary Current { get; private set; } = ProgressSummary.Empty;

    /// <summary>
    /// Stops the updates.
    /// </summary>
    public void Dispose()
    {
        progress.Changed -= OnChanged;
        timer.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (gate)
        {
            if (coolingDown)
            {
                changed = true;
                return;
            }

            coolingDown = true;
        }

        // The first change after a quiet spell goes out at once; the next ones wait for the interval.
        post(Publish);
        timer.Change(Interval, Timeout.InfiniteTimeSpan);
    }

    private void OnInterval()
    {
        lock (gate)
        {
            if (!changed && !active)
            {
                coolingDown = false;
                return;
            }

            changed = false;
        }

        post(Publish);
        timer.Change(Interval, Timeout.InfiniteTimeSpan);
    }

    private void Publish()
    {
        Current = progress.Current;
        active = Current.ActiveJobCount > 0;
        Updated?.Invoke(this, Current);
    }
}
