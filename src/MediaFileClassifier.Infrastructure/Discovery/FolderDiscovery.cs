using System.Diagnostics;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Discovery;

/// <summary>
/// Scans an indexed folder through <see cref="IMediaFileSource"/>, so the same code serves desktop paths and Android
/// folders. The folder's rules filter paths, the extension filters candidates, and the content decides the format
/// (E03-S01). New items are registered in batches through the library's writer (D072).
/// </summary>
/// <param name="files">Reads the granted folders.</param>
/// <param name="formats">The supported formats.</param>
/// <param name="time">Clock for the items' import time.</param>
/// <param name="logger">Logger.</param>
public sealed partial class FolderDiscovery(
    IMediaFileSource files,
    IMediaFormatRegistry formats,
    TimeProvider time,
    ILogger<FolderDiscovery> logger) : IFolderDiscovery
{
    /// <summary>
    /// Number of items written per transaction. It's the writer's batch size, measured in E01-S03-T04: a user edit
    /// queued behind ingestion waits about 20 ms (p50) with 100 items, against 170 ms with 500.
    /// </summary>
    public const int BatchSize = 100;

    private readonly IMediaFileSource files = files;
    private readonly IMediaFormatRegistry formats = formats;
    private readonly TimeProvider time = time;
    private readonly ILogger<FolderDiscovery> logger = logger;

    /// <summary>
    /// Gets the longest time a found item waits before it's written, so items show up while a slow folder is scanned.
    /// It's checked as each entry is scanned. One second by default.
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public async Task<DiscoveryResult> DiscoverAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IProgress<DiscoveryProgress>? progress = null,
        Func<IReadOnlyList<MediaItem>, CancellationToken, Task>? registered = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folder);

        var known = await library.Items.GetFileStatesAsync(folder.Id, cancellationToken).ConfigureAwait(false);
        var run = new Run(this, library, folder, known, progress, registered, collect: false);
        await run.ScanAsync(cancellationToken).ConfigureAwait(false);
        return run.ToResult();
    }

    /// <inheritdoc />
    public async Task<FolderSnapshot> SnapshotAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folder);

        var known = await library.Items.GetFileStatesAsync(folder.Id, cancellationToken).ConfigureAwait(false);
        var run = new Run(this, library, folder, known, progress, registered: null, collect: true);
        await run.ScanAsync(cancellationToken).ConfigureAwait(false);
        return new FolderSnapshot { Result = run.ToResult(), KnownFiles = run.KnownFiles, NewFiles = run.NewFiles };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discovery in {Folder}: can't list {Path}: {Error}")]
    private partial void LogUnreadableFolder(string folder, string path, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovery in {Folder}: skipped the link {Path}; links aren't followed")]
    private partial void LogSkippedLink(string folder, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discovery in {Folder}: can't read {Path}")]
    private partial void LogUnreadableFile(Exception exception, string folder, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discovery in {Folder}: skipped {Path}: {Reason}")]
    private partial void LogUnsupported(string folder, string path, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Discovery in {Folder} finished: {FoldersScanned} folders, {FilesFound} media files, {ItemsRegistered} new, {Unchanged} unchanged, "
            + "{Changed} changed, {CloudPlaceholdersSkipped} cloud placeholders skipped, {Unsupported} unsupported, {UnreadableFiles} unreadable files, {UnreadableFolders} unreadable folders, {SkippedLinks} links skipped")]
    private partial void LogFinished(
        string folder,
        int foldersScanned,
        int filesFound,
        int itemsRegistered,
        int unchanged,
        int changed,
        int cloudPlaceholdersSkipped,
        int unsupported,
        int unreadableFiles,
        int unreadableFolders,
        int skippedLinks);

    /// <summary>
    /// The state of one scan.
    /// </summary>
    private sealed class Run(
        FolderDiscovery owner,
        IOpenLibrary library,
        IndexedFolder folder,
        IReadOnlyList<MediaItemFileState> known,
        IProgress<DiscoveryProgress>? progress,
        Func<IReadOnlyList<MediaItem>, CancellationToken, Task>? registered,
        bool collect)
    {
        // Paths compare as stored: the scan reports a file's name the same way each time.
        private readonly Dictionary<string, MediaItemFileState> knownFiles = known.ToDictionary(state => state.Path.Value, StringComparer.Ordinal);
        private readonly FolderHandle handle = new(folder.PlatformHandle, folder.DisplayPath);
        private readonly FolderRuleMatcher rules = new(folder.Rules);
        private readonly List<MediaItem> pending = new(BatchSize);
        private readonly Stopwatch sincePendingStarted = new();
        private readonly List<string> unreadableFolders = [];
        private readonly List<string> skippedLinks = [];
        private readonly List<MediaFileStat> knownFound = [];
        private readonly List<MediaItem> newFound = [];
        private int foldersScanned;
        private int filesFound;
        private int itemsRegistered;
        private int unchanged;
        private int changed;
        private int cloudPlaceholdersSkipped;
        private int unsupported;
        private int unreadableFiles;

        public async Task ScanAsync(CancellationToken cancellationToken)
        {
            var options = new FolderScanOptions
            {
                IncludeSubfolders = folder.Rules.IncludeSubfolders,
                IncludeHidden = folder.Rules.IncludeHidden,
                ShouldEnterFolder = rules.ShouldEnterFolder,
            };

            await foreach (var entry in owner.files.ScanAsync(handle, options, cancellationToken).ConfigureAwait(false))
            {
                switch (entry.Kind)
                {
                    case FolderScanEntryKind.File:
                        await AddFileAsync(entry.File!, cancellationToken).ConfigureAwait(false);
                        break;
                    case FolderScanEntryKind.Folder:
                        foldersScanned++;
                        Report();
                        break;
                    case FolderScanEntryKind.UnreadableFolder:
                        unreadableFolders.Add(entry.RelativePath);
                        owner.LogUnreadableFolder(folder.DisplayPath, entry.RelativePath, entry.Error ?? string.Empty);
                        break;
                    case FolderScanEntryKind.SkippedLink:
                        skippedLinks.Add(entry.RelativePath);
                        owner.LogSkippedLink(folder.DisplayPath, entry.RelativePath);
                        break;
                }

                if (pending.Count > 0 && sincePendingStarted.Elapsed >= owner.FlushInterval)
                {
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await FlushAsync(cancellationToken).ConfigureAwait(false);
            owner.LogFinished(
                folder.DisplayPath,
                foldersScanned,
                filesFound,
                itemsRegistered,
                unchanged,
                changed,
                cloudPlaceholdersSkipped,
                unsupported,
                unreadableFiles,
                unreadableFolders.Count,
                skippedLinks.Count);
        }

        // Filled only by a snapshot (collect), which registers nothing.
        public IReadOnlyList<MediaFileStat> KnownFiles => knownFound;

        public IReadOnlyList<MediaItem> NewFiles => newFound;

        public DiscoveryResult ToResult() => new()
        {
            FoldersScanned = foldersScanned,
            FilesFound = filesFound,
            ItemsRegistered = itemsRegistered,
            Unchanged = unchanged,
            Changed = changed,
            CloudPlaceholdersSkipped = cloudPlaceholdersSkipped,
            Unsupported = unsupported,
            UnreadableFiles = unreadableFiles,
            UnreadableFolders = unreadableFolders,
            SkippedLinks = skippedLinks,
        };

        private async Task AddFileAsync(MediaFileStat file, CancellationToken cancellationToken)
        {
            if (!owner.formats.IsCandidate(file.RelativePath) || !rules.IsIncluded(file.RelativePath))
            {
                return;
            }

            // A file already in the library isn't read: unchanged ones are done, changed ones are the rescan's (E02-S04).
            if (knownFiles.TryGetValue(file.RelativePath, out var state))
            {
                filesFound++;
                if (collect)
                {
                    knownFound.Add(file);
                }

                if (state.SizeBytes == file.Length && state.FileModifiedAt == ToStoredPrecision(file.LastModifiedUtc))
                {
                    unchanged++;
                }
                else
                {
                    changed++;
                }

                return;
            }

            // Reading a placeholder downloads it, so it's skipped unless the folder includes them (E03-S02-T04).
            if (file.IsCloudPlaceholder && !folder.Rules.IncludeCloudPlaceholders)
            {
                cloudPlaceholdersSkipped++;
                return;
            }

            var path = ToRelativePath(file.RelativePath);
            if (path is null)
            {
                return;
            }

            var mediaType = await DetectAsync(file, cancellationToken).ConfigureAwait(false);
            if (mediaType is null)
            {
                return;
            }

            filesFound++;
            pending.Add(MediaItem.Discover(
                folder.Id,
                path,
                mediaType.Value,
                file.Length,
                file.CreatedUtc ?? file.LastModifiedUtc,
                file.LastModifiedUtc,
                fingerprint: null,
                owner.time.GetUtcNow()));
            if (pending.Count == 1)
            {
                sincePendingStarted.Restart();
            }

            if (pending.Count >= BatchSize)
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // A name the library can't store, such as a blank one, is skipped rather than stopping the scan.
        private RelativePath? ToRelativePath(string relativePath)
        {
            try
            {
                return RelativePath.From(relativePath);
            }
            catch (ArgumentException exception)
            {
                unsupported++;
                owner.LogUnsupported(folder.DisplayPath, relativePath, exception.Message);
                return null;
            }
        }

        // Reads the file's first bytes: the content decides the format, and only a supported one is registered.
        private async Task<MediaType?> DetectAsync(MediaFileStat file, CancellationToken cancellationToken)
        {
            MediaFormatDetection detection;
            try
            {
                var stream = await owner.files.OpenReadAsync(new FileLocation(handle, file.RelativePath), cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    detection = await owner.formats.DetectAsync(stream, file.RelativePath, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Locked, deleted since it was listed, or not readable by this user.
                unreadableFiles++;
                owner.LogUnreadableFile(exception, folder.DisplayPath, file.RelativePath);
                return null;
            }

            if (!detection.IsSupported)
            {
                unsupported++;
                owner.LogUnsupported(folder.DisplayPath, file.RelativePath, detection.Rejection!.ToString());
                return null;
            }

            return detection.MediaType;
        }

        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (pending.Count == 0)
            {
                return;
            }

            MediaItem[] batch = [.. pending];
            if (collect)
            {
                newFound.AddRange(batch);
                pending.Clear();
                return;
            }

            await library.Items.AddRangeAsync(batch, cancellationToken).ConfigureAwait(false);
            itemsRegistered += batch.Length;
            pending.Clear();
            Report();
            if (registered is not null)
            {
                await registered(batch, cancellationToken).ConfigureAwait(false);
            }
        }

        // Times are stored as Unix milliseconds (D071), so the file's time is cut to match before comparing.
        private static DateTimeOffset ToStoredPrecision(DateTimeOffset time) =>
            DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

        private void Report() => progress?.Report(new DiscoveryProgress(foldersScanned, filesFound, itemsRegistered));
    }
}
