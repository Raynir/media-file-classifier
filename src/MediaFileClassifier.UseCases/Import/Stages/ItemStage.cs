using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.Stages;

/// <summary>
/// An import stage for one media item. It loads the item and its folder, and when the stage throws, it marks the item
/// Error with the stage, the exception's type and whether the file itself is the problem (E03-S07-T02), then rethrows
/// so the work item fails too. Work for an item in an Offline folder waits for the folder instead (D188, D025).
/// </summary>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public abstract partial class ItemStage(TimeProvider clock, ILogger logger) : IFailureAwareStage
{
    /// <inheritdoc />
    public abstract string Stage { get; }

    /// <inheritdoc />
    public abstract ResourcePool Pool { get; }

    /// <summary>
    /// Gets the clock.
    /// </summary>
    protected TimeProvider Clock => clock;

    /// <summary>
    /// Gets the file source, which checks the folder's root when a file's directory is gone.
    /// </summary>
    protected abstract IMediaFileSource Files { get; }

    /// <summary>
    /// Decides whether a failure is the file's fault, so retrying won't help until the file changes.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><c>true</c> for a damaged or unsupported file; <c>false</c> for passing problems such as a locked file.</returns>
    public static bool IsPermanent(Exception exception) =>
        exception switch
        {
            MediaFileException file => file.IsPermanent,
            InvalidDataException or FormatException or NotSupportedException => true,
            _ => false,
        };

    /// <summary>
    /// Gets where an item's file is.
    /// </summary>
    /// <param name="folder">The item's folder.</param>
    /// <param name="item">The item.</param>
    /// <returns>The file's location for <see cref="IMediaFileSource"/>.</returns>
    public static FileLocation GetLocation(IndexedFolder folder, MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(item);

        return new FileLocation(new FolderHandle(folder.PlatformHandle, folder.DisplayPath), item.Path.Value);
    }

    /// <summary>
    /// Gets the file's path on disk when the folder is a local path, as on the desktop, so FFmpeg can seek in it;
    /// otherwise <c>null</c>, e.g. for an Android SAF folder.
    /// </summary>
    /// <param name="folder">The item's folder.</param>
    /// <param name="item">The item.</param>
    /// <returns>The full path, or <c>null</c>.</returns>
    public static string? GetLocalPath(IndexedFolder folder, MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(item);

        return Path.IsPathFullyQualified(folder.PlatformHandle)
            ? Path.Combine(folder.PlatformHandle, item.Path.Value.Replace('/', Path.DirectorySeparatorChar))
            : null;
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var itemId = context.WorkItem.ItemId ?? throw new InvalidOperationException($"A {Stage} work item needs an item.");
        var library = context.Library;
        var item = await library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);

        // Removed or gone since the work was queued: there's nothing to do.
        if (item is null || item.Status is MediaItemStatus.Missing)
        {
            return;
        }

        var folder = await library.Folders.GetAsync(item.FolderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The item's folder isn't in the library.");

        // An excluded item is imported again only once its folder's rules include it (E02-S03-T03).
        if (item.Status == MediaItemStatus.Excluded && !IndexedFolders.IsBackInRules(item, new FolderRuleMatcher(folder.Rules)))
        {
            return;
        }

        OfflineFolders.ThrowIfOffline(folder);
        try
        {
            await ProcessAsync(context, item, folder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OfflineFolders.IsMissingDirectory(exception))
        {
            await OfflineFolders.ThrowForMissingDirectoryAsync(library, folder, Files, clock, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Marks the item as failed, once the scheduler gave up on it (E05-S04): transient failures are retried first.
    /// </summary>
    /// <param name="context">The work item and the open library.</param>
    /// <param name="error">The failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the item is marked.</returns>
    public async Task OnFailedAsync(WorkItemContext context, ClassifiedError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        if (context.WorkItem.ItemId is { } itemId)
        {
            await RecordFailureAsync(context, itemId, error, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the stage for one item, and queues the next stage. Stages store their result once, at the end, and skip
    /// work that's already done, so a stage run again after an interruption creates nothing twice.
    /// </summary>
    /// <param name="context">The work item and the open library.</param>
    /// <param name="item">The item, as stored.</param>
    /// <param name="folder">The item's folder.</param>
    /// <param name="cancellationToken">Cancelled when the job is cancelled or the library closes.</param>
    /// <returns>A task that completes when the stage is done.</returns>
    protected abstract Task ProcessAsync(WorkItemContext context, MediaItem item, IndexedFolder folder, CancellationToken cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import stage {Stage} failed for {Path}; the item is marked as failed")]
    private partial void LogItemFailed(Exception exception, string stage, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't mark {Path} as failed in import stage {Stage}")]
    private partial void LogFailureNotRecorded(Exception exception, string stage, string path);

    // The stage may have changed the item in memory, so the stored item is marked, not that one.
    private async Task RecordFailureAsync(WorkItemContext context, Id<MediaItem> itemId, ClassifiedError error, CancellationToken cancellationToken)
    {
        var exception = error.Exception;
        var path = itemId.ToString();
        try
        {
            var stored = await context.Library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (stored is null || !MediaItemStatusTransitions.IsAllowed(stored.Status, MediaItemStatus.Error))
            {
                return;
            }

            path = stored.Path.Value;
            LogItemFailed(exception, Stage, path);
            var itemError = new MediaItemError(Stage, exception is MediaFileException ? null : exception.GetType().Name, error.Category == ErrorCategory.Permanent);
            stored.MarkFailed(itemError, error.Message, clock.GetUtcNow());
            await context.Library.Items.UpdateAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception recordException) when (recordException is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailureNotRecorded(recordException, Stage, path);
        }
    }
}
