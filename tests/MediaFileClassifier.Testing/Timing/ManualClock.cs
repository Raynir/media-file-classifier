namespace MediaFileClassifier.Testing.Timing;

/// <summary>
/// A clock whose time moves and whose one-shot and periodic timers fire only when the test advances it. Timers fire
/// on the test's thread, in due order. Timers may be created and changed from other threads while it advances. A period
/// of zero or less is one-shot, like <see cref="System.Threading.Timer"/>'s.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private readonly Lock syncRoot = new();
    private readonly DateTimeOffset start;
    private TimeSpan now;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualClock"/> class.
    /// </summary>
    /// <param name="start">The time it starts at; the Unix epoch by default.</param>
    public ManualClock(DateTimeOffset? start = null) => this.start = start ?? DateTimeOffset.UnixEpoch;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (syncRoot)
        {
            return start + now;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (syncRoot)
        {
            timers.Add(timer);
        }

        return timer;
    }

    /// <summary>
    /// Moves the time on, firing every timer that comes due on the way.
    /// </summary>
    /// <param name="time">How far.</param>
    public void Advance(TimeSpan time)
    {
        TimeSpan end;
        lock (syncRoot)
        {
            end = now + time;
        }

        while (true)
        {
            ManualTimer? next;
            lock (syncRoot)
            {
                next = timers.Where(timer => timer.DueAt <= end).MinBy(timer => timer.DueAt);
                if (next is null)
                {
                    now = end;
                    return;
                }

                now = next.DueAt!.Value;
                next.Advance();
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan period = Timeout.InfiniteTimeSpan;

        public TimeSpan? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.syncRoot)
            {
                this.period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
            }

            return true;
        }

        // Called under the clock's lock: sets the next due time before the callback runs.
        public void Advance() => DueAt = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : clock.now + period;

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock.syncRoot)
            {
                DueAt = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
