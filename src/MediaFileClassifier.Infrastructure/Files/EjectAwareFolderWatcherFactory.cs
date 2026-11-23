using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// Creates watchers that let go of their folder when the user ejects its drive (E04-S04-T01), so a watched USB drive can
/// be ejected safely. While the eject is under way the folder isn't watched; if the eject is cancelled, the folder is
/// watched again and the watcher reports lost events, so the folder is rescanned (D517). If the drive goes, its volume
/// event marks the folder Offline and the watcher is disposed.
/// </summary>
/// <param name="inner">Creates the watchers that do the watching.</param>
/// <param name="ejects">Reports the eject requests.</param>
public sealed class EjectAwareFolderWatcherFactory(IFolderWatcherFactory inner, IEjectNotifications ejects) : IFolderWatcherFactory
{
    /// <inheritdoc />
    public IFolderWatcher Watch(FolderHandle folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var watcher = new EjectAwareFolderWatcher(inner, folder);
        try
        {
            watcher.Listen(ejects.Register(folder));
        }
        catch
        {
            watcher.Dispose();
            throw;
        }

        return watcher;
    }

    private sealed class EjectAwareFolderWatcher : IFolderWatcher
    {
        private readonly IFolderWatcherFactory factory;
        private readonly FolderHandle folder;
        private readonly Lock syncRoot = new();
        private IFolderWatcher? current;
        private IEjectRegistration? registration;
        private bool released;
        private bool disposed;

        public EjectAwareFolderWatcher(IFolderWatcherFactory factory, FolderHandle folder)
        {
            this.factory = factory;
            this.folder = folder;

            // Held while starting, so an event raised at once waits until the watcher is known as the current one.
            lock (syncRoot)
            {
                current = Start();
            }
        }

        public event EventHandler<FolderWatchEvent>? Changed;

        public event EventHandler<FolderWatchErrorEventArgs>? Failed;

        public void Listen(IEjectRegistration? ejectRegistration)
        {
            if (ejectRegistration is null)
            {
                return;
            }

            lock (syncRoot)
            {
                if (disposed)
                {
                    ejectRegistration.Dispose();
                    return;
                }

                registration = ejectRegistration;
                registration.EjectRequested += OnEjectRequested;
                registration.EjectCancelled += OnEjectCancelled;
            }
        }

        public void Dispose()
        {
            IFolderWatcher? watcher;
            IEjectRegistration? listening;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                (watcher, current) = (current, null);
                (listening, registration) = (registration, null);
            }

            if (listening is not null)
            {
                listening.EjectRequested -= OnEjectRequested;
                listening.EjectCancelled -= OnEjectCancelled;
                listening.Dispose();
            }

            watcher?.Dispose();
        }

        private IFolderWatcher Start()
        {
            var watcher = factory.Watch(folder);
            watcher.Changed += OnChanged;
            watcher.Failed += OnFailed;
            return watcher;
        }

        // Runs synchronously inside the eject request: the folder must be closed before it returns.
        private void OnEjectRequested(object? sender, EventArgs e)
        {
            IFolderWatcher? watcher;
            lock (syncRoot)
            {
                if (disposed || released)
                {
                    return;
                }

                released = true;
                (watcher, current) = (current, null);
            }

            watcher?.Dispose();
        }

        private void OnEjectCancelled(object? sender, EventArgs e)
        {
            FolderWatchErrorEventArgs failure;
            lock (syncRoot)
            {
                if (disposed || !released)
                {
                    return;
                }

                released = false;
                try
                {
                    current = Start();

                    // Changes made while the folder wasn't watched were missed, so it's rescanned.
                    failure = new FolderWatchErrorEventArgs(isOverflow: true, exception: null);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Not watched now: the folder's availability is checked, as for any failed watcher.
                    failure = new FolderWatchErrorEventArgs(isOverflow: false, exception);
                }
            }

            Failed?.Invoke(this, failure);
        }

        private void OnChanged(object? sender, FolderWatchEvent change)
        {
            if (IsCurrent(sender))
            {
                Changed?.Invoke(this, change);
            }
        }

        private void OnFailed(object? sender, FolderWatchErrorEventArgs error)
        {
            if (IsCurrent(sender))
            {
                Failed?.Invoke(this, error);
            }
        }

        // A watcher released for an eject can still raise an event it was raising; it's no longer this one's.
        private bool IsCurrent(object? sender)
        {
            lock (syncRoot)
            {
                return !disposed && ReferenceEquals(sender, current);
            }
        }
    }
}
