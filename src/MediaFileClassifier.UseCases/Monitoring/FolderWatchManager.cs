using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
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
/// the stable changes are published as <see cref="FolderChangesDetected"/> (E04-S01-T02). Paths the ignore list
/// (<see cref="MonitoringSettings.IgnorePatterns"/>), the folder's rules or the supported formats leave out are dropped
/// as they arrive; a partial folder (D012) reports only its tracked files, and changes to excluded items (E02-S10) aren't
/// reported (E04-S01-T03).
/// </summary>
public sealed class FolderWatchManager : IDisposable
{
    private readonly ILibrarySession session;
    private readonly IFolderWatcherFactory? factory;
    private readonly IPublisher publisher;
    private readonly ISettingsStore settings;
    private readonly IMediaFormatRegistry formats;
    private readonly ILogger<FolderWatchManager> logger;
    private readonly FolderChangeBuffer buffer;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<IndexedFolder>, WatchedFolder> watched = [];

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A refresh already under way may still use it after Dispose; it holds no OS handle unless its wait handle is read.")]
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Cancelled by Dispose; a refresh already under way may still read its token. It has no timer or wait handle.")]
    private readonly CancellationTokenSource stopping = new();

    private volatile IgnoreList ignore;
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
    /// <param name="settings">Holds the ignore list.</param>
    /// <param name="formats">Tells which files are media files.</param>
    /// <param name="clock">Times the debounce and the stability checks.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="factory">Starts the watchers, or <see langword="null"/> where the platform can't watch folders.</param>
    public FolderWatchManager(
        ILibrarySession session,
        IMediaFileSource files,
        IPublisher publisher,
        ISettingsStore settings,
        IMediaFormatRegistry formats,
        TimeProvider clock,
        ILogger<FolderWatchManager> logger,
        IFolderWatcherFactory? factory = null)
    {
        this.session = session;
        this.publisher = publisher;
        this.settings = settings;
        this.formats = formats;
        ignore = new IgnoreList(settings.Get(MonitoringSettings.IgnorePatterns));
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
            settings.Changed += OnSettingChanged;
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
            settings.Changed -= OnSettingChanged;
        }

        Follow(null);
        buffer.Dispose();
        stopping.Cancel();
    }

    private void RequestRefresh() => _ = RefreshAsync();

    private static Task<MediaItem?> FindAsync(IOpenLibrary current, Id<IndexedFolder> folderId, string path, CancellationToken cancellationToken) =>
        current.Items.FindByPathAsync(folderId, RelativePath.From(path), cancellationToken);

    private void OnLibraryChanged(object? sender, EventArgs e) => RequestRefresh();

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting.Key == MonitoringSettings.IgnorePatterns.Key)
        {
            ignore = new IgnoreList(settings.Get(MonitoringSettings.IgnorePatterns));
        }
    }

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

        if (Filter(folder.Folder, change) is { } kept)
        {
            buffer.Record(folder.Folder.Id, folder.Handle, kept);
        }
    }

    /// <summary>
    /// Drops what the ignore list, the folder's rules or the supported formats leave out. A rename between a path that's
    /// left out and one that isn't is a new file or a deletion.
    /// </summary>
    private FolderWatchEvent? Filter(IndexedFolder folder, FolderWatchEvent change)
    {
        var included = Includes(folder, change.RelativePath, change.IsDirectory);
        if (change is not { Kind: FolderWatchEventKind.Renamed, OldRelativePath: { } oldPath })
        {
            return included ? change : null;
        }

        return (Includes(folder, oldPath, change.IsDirectory), included) switch
        {
            (true, true) => change,
            (false, true) => change with { Kind = FolderWatchEventKind.Created, OldRelativePath = null },
            (true, false) => new FolderWatchEvent(FolderWatchEventKind.Deleted, oldPath),
            _ => null,
        };
    }

    private bool Includes(IndexedFolder folder, string path, bool isDirectory)
    {
        if (ignore.IsIgnored(path))
        {
            return false;
        }

        // A partial folder's rules don't apply (D012); its tracked files are checked when the change is ready.
        if (!folder.SelectedFilesOnly)
        {
            var rules = new FolderRuleMatcher(folder.Rules);
            if (isDirectory ? !rules.ShouldEnterFolder(path) : !rules.IsIncluded(path))
            {
                return false;
            }

            if (!folder.Rules.IncludeSubfolders && path.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }
        }

        // A deletion's path may be a folder, so it isn't checked for a media extension.
        return isDirectory || formats.IsCandidate(path);
    }

    /// <summary>
    /// Keeps the ready changes that the library should hear about: hidden files and cloud placeholders only where the
    /// rules include them, a partial folder's tracked files only, and nothing about excluded items.
    /// </summary>
    private async Task<IReadOnlyList<FolderChange>> KeepAsync(IndexedFolder folder, IOpenLibrary current, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        var kept = new List<FolderChange>(changes.Count);
        foreach (var change in changes)
        {
            if (change.Stat is { } stat
                && ((stat.IsHidden && !folder.Rules.IncludeHidden) || (stat.IsCloudPlaceholder && !folder.Rules.IncludeCloudPlaceholders)))
            {
                if (change is { Kind: FolderChangeKind.Renamed, OldRelativePath: { } hiddenFrom })
                {
                    kept.Add(new FolderChange(FolderChangeKind.Deleted, hiddenFrom));
                }

                continue;
            }

            var item = await FindAsync(current, folder.Id, change.OldRelativePath ?? change.RelativePath, cancellationToken).ConfigureAwait(false);
            if (folder.SelectedFilesOnly && item is null && (change.OldRelativePath is null
                || await FindAsync(current, folder.Id, change.RelativePath, cancellationToken).ConfigureAwait(false) is null))
            {
                continue;
            }

            if (item?.Status == MediaItemStatus.Excluded)
            {
                continue;
            }

            kept.Add(change);
        }

        return kept;
    }

    private async Task PublishAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        IndexedFolder folder;
        IOpenLibrary current;
        lock (syncRoot)
        {
            // Stopped being watched during the checks, e.g. it went Offline: its changes are found when it's checked again.
            if (!watched.TryGetValue(folderId, out var entry) || library is null)
            {
                return;
            }

            folder = entry.Folder;
            current = library;
        }

        changes = await KeepAsync(folder, current, changes, cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return;
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
