using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;
using MediatR;

namespace MediaFileClassifier.Ui.Libraries.Statistics;

/// <summary>
/// Keeps the open library's statistics (E02-S05) up to date for the status bar summary and the library information
/// view. It counts again when a library opens or closes, and at most once per <see cref="RefreshInterval"/> while
/// items change, so an import or analysis costs one aggregate query a second (about 45 ms at 100k items).
/// <see cref="Changed"/> is raised on the UI thread.
/// </summary>
public sealed class LibraryStatisticsMonitor : IDisposable
{
    /// <summary>
    /// The shortest time between two counts while items change.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly ISender sender;
    private readonly ILibrarySession session;
    private readonly Action<Action> post;
    private readonly ITimer timer;
    private IMediaItemRepository? watched;
    private bool started;
    private bool refreshPending;
    private int version;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryStatisticsMonitor"/> class.
    /// </summary>
    /// <param name="sender">Runs the statistics query.</param>
    /// <param name="session">Holds the open library.</param>
    /// <param name="clock">Times the throttled refresh.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryStatisticsMonitor(ISender sender, ILibrarySession session, TimeProvider clock, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.sender = sender;
        this.session = session;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        timer = clock.CreateTimer(_ => this.post(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised on the UI thread when <see cref="Current"/> changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest statistics, or <see langword="null"/> while no library is open.
    /// </summary>
    public LibraryStatistics? Current { get; private set; }

    /// <summary>
    /// Starts following the open library, once; later calls do nothing. Call it on the UI thread.
    /// </summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        session.CurrentChanged += OnLibraryChanged;
        Watch();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Counts again now. Call it on the UI thread.
    /// </summary>
    /// <returns>A task that completes when the statistics are counted.</returns>
    public async Task RefreshAsync()
    {
        var current = ++version;
        LibraryStatistics? statistics;
        try
        {
            statistics = await sender.Send(new GetLibraryStatisticsQuery());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The library closed under us, or can't be read: keep the last figures, the next change counts again.
            return;
        }

        // A newer refresh already asked, so this answer may be stale.
        if (current != version || (Equals(statistics, Current) && !libraryChanged))
        {
            return;
        }

        libraryChanged = false;

        Current = statistics;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.CurrentChanged -= OnLibraryChanged;
        if (watched is not null)
        {
            watched.Changed -= OnItemsChanged;
        }

        timer.Dispose();
    }

    // Another library is open: report its counts even when they equal the previous library's, so views reload.
    private bool libraryChanged;

    private void OnLibraryChanged(object? sender, EventArgs e) => post(() =>
    {
        Watch();
        _ = RefreshAsync();
    });

    private void Watch()
    {
        var items = session.Current?.Items;
        if (ReferenceEquals(items, watched))
        {
            return;
        }

        if (watched is not null)
        {
            watched.Changed -= OnItemsChanged;
        }

        watched = items;
        libraryChanged = true;
        if (watched is not null)
        {
            watched.Changed += OnItemsChanged;
        }
    }

    // Raised on the writing thread. The first change starts the timer; the count at its tick covers the changes made
    // meanwhile.
    private void OnItemsChanged(object? sender, EventArgs e) => post(() =>
    {
        if (refreshPending)
        {
            return;
        }

        refreshPending = true;
        timer.Change(RefreshInterval, Timeout.InfiniteTimeSpan);
    });

    private void OnTimer()
    {
        refreshPending = false;
        _ = RefreshAsync();
    }
}
