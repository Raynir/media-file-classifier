using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Keeps one recursive watcher on each Online folder of the open library (E04-S01-T01). The watchers follow the
/// library opening and closing, folders being added, changed, merged or removed, and folders going Offline and coming
/// back. A folder being removed isn't watched. Where the platform can't watch folders (no
/// <see cref="IFolderWatcherFactory"/>), it does nothing. Raw events go through a <see cref="FolderChangeBuffer"/>, and
/// the stable changes are published as <see cref="FolderChangesDetected"/> (E04-S01-T02).
/// </summary>
public sealed class FolderWatchManager : IDisposable
{
    private readonly ILibrarySession session;
    private readonly IFolderWatcherFactory? factory;
    private readonly IPublisher publisher;
    private readonly ILogger<FolderWatchManager> logger;
    private readonly FolderChangeBuffer buffer;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<IndexedFolder>, WatchedFolder> watched = [];

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A refresh already under way may still use it after Dispose; it holds no OS handle unless its wait handle is read.")]
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a refresh already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private IOpenLibrary? library;
    private IFolderRepository? folderEvents;
    private bool started;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderWatchManager"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="files">Checks that changed files are stable.</param>
    /// <param name="publisher">Publishes the stable changes.</param>
    /// <param name="clock">Times the debounce and the stability checks.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="factory">Starts the watchers, or <see langword="null"/> where the platform can't watch folders.</param>
    public FolderWatchManager(
        ILibrarySession session,
        IMediaFileSource files,
        IPublisher publisher,
        TimeProvider clock,
        ILogger<FolderWatchManager> logger,
        IFolderWatcherFactory? factory = null)
    {
        this.session = session;
        this.publisher = publisher;
        this.logger = logger;
        this.factory = factory;
        buffer = new FolderChangeBuffer(files, clock, logger, PublishAsync);
    }

    /// <summary>
    /// Gets the folders watched now.
    /// </summary>
    public IReadOnlyCollection<Id<IndexedFolder>> WatchedFolders
    {
        get
        {
            lock (syncRoot)
            {
                return [.. watched.Keys];
            }
        }
    }

    /// <summary>
    /// Starts following the open library. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started || factory is null)
            {
                return;
            }

            started = true;
            session.CurrentChanged += OnLibraryChanged;
        }

        RequestRefresh();
    }

    /// <summary>
    /// Brings the watchers in line with the open library's Online folders now. Refreshes run one at a time; the folder
    /// list is read inside the refresh, so a change that raised an event while one ran is seen by the next.
    /// </summary>
    /// <returns>A task that completes when the watchers match the folders.</returns>
    public async Task RefreshAsync()
    {
        var token = stopping.Token;
        try
        {
            await refreshGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var current = session.Current;
            Follow(current);
            if (current is null)
            {
                return;
            }

            var folders = (await IndexedFolders.GetActiveAsync(current, token).ConfigureAwait(false))
                .Where(folder => folder.Status == FolderStatus.Online)
                .ToList();
            Apply(current, folders);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // E.g. the library closed during the read. Its closing raises a refresh of its own.
            logger.LogWarning(exception, "Couldn't update the folder watchers");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Stops every watcher and stops following the library.
    /// </summary>
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            session.CurrentChanged -= OnLibraryChanged;
        }

        Follow(null);
        buffer.Dispose();
        stopping.Cancel();
    }

    private void RequestRefresh() => _ = RefreshAsync();

    private void OnLibraryChanged(object? sender, EventArgs e) => RequestRefresh();

    private void OnFoldersChanged(object? sender, EventArgs e)
    {
        lock (syncRoot)
        {
            // The repository raises from its own copy of the handlers, so a closed library's event can still arrive.
            if (disposed || !ReferenceEquals(sender, folderEvents))
            {
                return;
            }
        }

        RequestRefresh();
    }

    /// <summary>
    /// Follows the folder events of the open library. When the library changed, every watcher of the old one stops.
    /// </summary>
    private void Follow(IOpenLibrary? current)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                current = null;
            }

            if (ReferenceEquals(current, library))
            {
                return;
            }

            if (folderEvents is not null)
            {
                folderEvents.Changed -= OnFoldersChanged;
                folderEvents.StatusChanged -= OnFoldersChanged;
            }

            foreach (var folder in watched.Values)
            {
                folder.Stop();
                buffer.Forget(folder.Folder.Id);
            }

            watched.Clear();
            library = current;
            folderEvents = current?.Folders;
            if (folderEvents is not null)
            {
                folderEvents.Changed += OnFoldersChanged;
                folderEvents.StatusChanged += OnFoldersChanged;
            }
        }
    }

    private void Apply(IOpenLibrary current, IReadOnlyList<IndexedFolder> folders)
    {
        lock (syncRoot)
        {
            // Another library opened during the read; its own refresh is queued.
            if (disposed || !ReferenceEquals(current, library))
            {
                return;
            }

            var wanted = folders.ToDictionary(folder => folder.Id);
            foreach (var gone in watched.Values.Where(folder => !wanted.ContainsKey(folder.Folder.Id)).ToList())
            {
                gone.Stop();
                watched.Remove(gone.Folder.Id);
                buffer.Forget(gone.Folder.Id);
                logger.LogInformation("Stopped watching {Folder}", gone.Folder.DisplayPath);
            }

            foreach (var folder in folders)
            {
                if (watched.TryGetValue(folder.Id, out var existing))
                {
                    if (existing.Root == folder.PlatformHandle)
                    {
                        // Rules or partial-folder mode may have changed; the watcher stays.
                        existing.Folder = folder;
                        continue;
                    }

                    // Relinked to a new root.
                    existing.Stop();
                    watched.Remove(folder.Id);
                    buffer.Forget(folder.Id);
                }

                if (StartWatching(folder) is { } started)
                {
                    watched[folder.Id] = started;
                }
            }
        }
    }

    private WatchedFolder? StartWatching(IndexedFolder folder)
    {
        try
        {
            var watcher = factory!.Watch(folder.ToHandle());
            var entry = new WatchedFolder(folder, watcher);
            watcher.Changed += (_, change) => OnChanged(entry, change);
            logger.LogInformation("Watching {Folder}", folder.DisplayPath);
            return entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            logger.LogWarning(exception, "Can't watch {Folder}", folder.DisplayPath);
            return null;
        }
    }

    private void OnChanged(WatchedFolder folder, FolderWatchEvent change)
    {
        if (folder.IsStopped)
        {
            return;
        }

        buffer.Record(folder.Folder.Id, folder.Handle, change);
    }

    private async Task PublishAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        lock (syncRoot)
        {
            // Stopped being watched during the checks, e.g. it went Offline: what's left is the rescan's to find.
            if (!watched.ContainsKey(folderId))
            {
                return;
            }
        }

        logger.LogInformation("{Count} changes in watched folder {Folder}", changes.Count, folderId);
        await publisher.Publish(new FolderChangesDetected(folderId, changes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A watched folder and its watcher.
    /// </summary>
    private sealed class WatchedFolder(IndexedFolder folder, IFolderWatcher watcher)
    {
        private volatile IndexedFolder folder = folder;
        private volatile bool stopped;

        /// <summary>
        /// Gets or sets the folder as last read, with its current rules.
        /// </summary>
        public IndexedFolder Folder
        {
            get => folder;
            set => folder = value;
        }

        /// <summary>
        /// Gets the root the watcher was started on.
        /// </summary>
        public string Root { get; } = folder.PlatformHandle;

        /// <summary>
        /// Gets the handle the watcher was started on.
        /// </summary>
        public FolderHandle Handle { get; } = folder.ToHandle();

        public bool IsStopped => stopped;

        public void Stop()
        {
            stopped = true;
            watcher.Dispose();
        }
    }
}
