using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Reports that an indexed folder of the open library went Offline or came back Online (E14-S07-T04), so what shows its
/// items' availability refreshes: the results, the details panel and the offline folders notice. A folder's status isn't
/// part of its items' rows, so <c>IMediaItemRepository.Changed</c> doesn't cover it. It follows the session and passes on
/// <see cref="IFolderRepository.StatusChanged"/> of the open library, so every writer of a folder's status is covered:
/// availability checks, discovery, rescans and imports.
/// </summary>
/// <remarks>
/// A burst of status changes, e.g. one availability check that finds a hub's folders offline, or one volume event, is
/// reported once, <see cref="Quiet"/> after the last change of the burst (MFC-1014). So the results re-query and the
/// details panel reloads once, not once per folder.
/// </remarks>
public sealed class FolderStatusChanges : IDisposable
{
    /// <summary>
    /// How long folder statuses must stay unchanged before a burst of changes is reported.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly ILibrarySession? session;
    private readonly ITimer timer;
    private readonly Lock syncRoot = new();
    private IFolderRepository? watched;
    private bool pending;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderStatusChanges"/> class.
    /// </summary>
    /// <param name="session">Holds the open library whose folders are watched, or <see langword="null"/> to rely on
    /// <see cref="Raise"/> alone.</param>
    /// <param name="clock">Times the wait for a burst of changes to end, or <see langword="null"/> for the system
    /// clock.</param>
    public FolderStatusChanges(ILibrarySession? session = null, TimeProvider? clock = null)
    {
        timer = (clock ?? TimeProvider.System).CreateTimer(_ => OnQuiet(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        this.session = session;
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Watch();
            Watch();
        }
    }

    /// <summary>
    /// Raised after folders of the open library changed status: once per burst of stored changes, <see cref="Quiet"/>
    /// after the last one, on a timer thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raises <see cref="Changed"/> now.
    /// </summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending = false;
            if (watched is not null)
            {
                watched.StatusChanged -= OnStatusChanged;
                watched = null;
            }
        }

        timer.Dispose();
    }

    private void Watch()
    {
        lock (syncRoot)
        {
            var folders = session?.Current?.Folders;
            if (disposed || ReferenceEquals(folders, watched))
            {
                return;
            }

            if (watched is not null)
            {
                watched.StatusChanged -= OnStatusChanged;
            }

            // A change of the library that's closing isn't the new one's to report.
            pending = false;
            watched = folders;
            if (watched is not null)
            {
                watched.StatusChanged += OnStatusChanged;
            }
        }
    }

    // Each change restarts the wait, so the burst is reported once it's over.
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            pending = true;
            timer.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    // A change stored while this runs either is seen by the listeners' refresh, or restarts the wait and is reported
    // again: none is lost.
    private void OnQuiet()
    {
        lock (syncRoot)
        {
            if (!pending)
            {
                return;
            }

            pending = false;
        }

        Raise();
    }
}
