using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.RemoveFolder;

/// <summary>
/// The stage of <see cref="IndexedFolders.RemoveJob"/>: deletes the job's folder from the library, in short batches so
/// other writes get their turn. Interrupted, it carries on where it stopped when the library opens again.
/// </summary>
/// <param name="thumbnails">Deletes the thumbnails.</param>
/// <param name="sender">Records the event in the activity log, or <see langword="null"/> for none.</param>
public sealed class RemoveFolderStage(IThumbnailCache thumbnails, ISender? sender = null) : IStageHandler
{
    /// <summary>
    /// Number of items deleted per write.
    /// </summary>
    public const int BatchSize = 500;

    /// <inheritdoc />
    public string Stage => IndexedFolders.RemoveStage;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <summary>
    /// Deletes a folder's items with their thumbnails, then the folder. Files on disk are never touched.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="thumbnails">Deletes the thumbnails.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already deleted stay deleted.</param>
    /// <returns>The number of items deleted.</returns>
    public static async Task<int> RemoveAsync(IOpenLibrary library, IThumbnailCache thumbnails, Id<IndexedFolder> folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(thumbnails);

        // Thumbnails first: an interrupted removal finds the items left and deletes their thumbnails again, but a
        // thumbnail whose item is gone would never be found.
        foreach (var item in await library.Items.GetByFolderAsync(folderId, cancellationToken).ConfigureAwait(false))
        {
            thumbnails.Invalidate(library.Layout, item.Id);
        }

        var deleted = 0;
        while (true)
        {
            var batch = await library.Items.DeleteFromFolderAsync(folderId, BatchSize, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            deleted += batch.Count;
        }

        await library.Folders.DeleteAsync(folderId, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var job = await context.Library.Jobs.GetJobAsync(context.WorkItem.JobId, cancellationToken).ConfigureAwait(false);
        var folderId = job?.FolderId ?? throw new InvalidOperationException("A folder removal needs a folder.");
        var path = (await context.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false))?.DisplayPath;
        var removed = await RemoveAsync(context.Library, thumbnails, folderId, cancellationToken).ConfigureAwait(false);
        await RecordAsync(sender, path, removed).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that a folder was removed in the activity log (E15-S04).
    /// </summary>
    /// <param name="sender">Records the event, or <see langword="null"/> for none.</param>
    /// <param name="path">The folder's path.</param>
    /// <param name="items">How many items went with it.</param>
    /// <returns>A task that completes when the event is recorded.</returns>
    internal static async Task RecordAsync(ISender? sender, string? path, int items)
    {
        if (sender is not null && path is not null)
        {
            await sender.Send(
                new RecordActivityCommand(
                    ActivityEventType.FolderRemoved,
                    Subject: path,
                    Counts: new Dictionary<string, long> { [ActivityCounts.Items] = items }),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
