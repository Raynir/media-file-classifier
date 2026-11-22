namespace MediaFileClassifier.Testing.Timing;

/// <summary>
/// A clock whose time moves and whose one-shot and periodic timers fire only when the test advances it. Timers fire
/// on the test's thread, in due order.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private readonly DateTimeOffset start;
    private TimeSpan now;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualClock"/> class.
    /// </summary>
    /// <param name="start">The time it starts at; the Unix epoch by default.</param>
    public ManualClock(DateTimeOffset? start = null) => this.start = start ?? DateTimeOffset.UnixEpoch;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => start + now;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        timers.Add(timer);
        return timer;
    }

    /// <summary>
    /// Moves the time on, firing every timer that comes due on the way.
    /// </summary>
    /// <param name="time">How far.</param>
    public void Advance(TimeSpan time)
    {
        var end = now + time;
        while (timers.Where(timer => timer.DueAt <= end).MinBy(timer => timer.DueAt) is { } next)
        {
            now = next.DueAt!.Value;
            next.Fire();
        }

        now = end;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan period = Timeout.InfiniteTimeSpan;

        public TimeSpan? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            this.period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = period == Timeout.InfiniteTimeSpan ? null : clock.now + period;
            callback(state);
        }

        public void Dispose() => DueAt = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
