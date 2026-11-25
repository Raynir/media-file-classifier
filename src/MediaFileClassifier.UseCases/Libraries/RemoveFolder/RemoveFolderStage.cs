using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <param name="batchDeleted">Called after each batch with the number of items deleted so far by this call, or <see langword="null"/>.</param>
    /// <returns>The number of items deleted.</returns>
    public static async Task<int> RemoveAsync(
        IOpenLibrary library,
        IThumbnailCache thumbnails,
        Id<IndexedFolder> folderId,
        CancellationToken cancellationToken,
        Func<int, CancellationToken, Task>? batchDeleted = null)
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
            if (batchDeleted is not null)
            {
                await batchDeleted(deleted, cancellationToken).ConfigureAwait(false);
            }
        }

        await library.Folders.DeleteAsync(folderId, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // After an interruption the earlier runs' deletions aren't counted by this one, and the folder row may be gone,
        // which also clears the job's folder. So the folder, its path and the running total are kept in the work item's
        // payload (MFC-1037).
        var progress = RemovalProgress.Read(context.WorkItem.Payload);
        var job = await context.Library.Jobs.GetJobAsync(context.WorkItem.JobId, cancellationToken).ConfigureAwait(false);
        var folderId = job?.FolderId ?? (progress is null ? (Id<IndexedFolder>?)null : new Id<IndexedFolder>(progress.FolderId))
            ?? throw new InvalidOperationException("A folder removal needs a folder.");
        var path = progress?.Path;
        var before = progress?.Removed ?? 0;
        if (path is null)
        {
            path = (await context.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false))?.DisplayPath;
            if (path is not null)
            {
                await SaveAsync(context, new RemovalProgress(folderId.Value, path, before), cancellationToken).ConfigureAwait(false);
            }
        }

        var removed = await RemoveAsync(
            context.Library,
            thumbnails,
            folderId,
            cancellationToken,
            (deleted, token) => path is null ? Task.CompletedTask : SaveAsync(context, new RemovalProgress(folderId.Value, path, before + deleted), token)).ConfigureAwait(false);
        await RecordAsync(sender, path, before + removed).ConfigureAwait(false);
    }

    private static Task SaveAsync(WorkItemContext context, RemovalProgress progress, CancellationToken cancellationToken) =>
        context.Library.Jobs.SetWorkItemPayloadAsync(context.WorkItem.Id, JsonSerializer.Serialize(progress), cancellationToken);

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

    /// <summary>
    /// What a removal remembers across an interruption, stored as the work item's payload.
    /// </summary>
    /// <param name="FolderId">The folder.</param>
    /// <param name="Path">The folder's path.</param>
    /// <param name="Removed">The items deleted so far, in all runs.</param>
    private sealed record RemovalProgress(
        [property: JsonPropertyName("folder")] Guid FolderId,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("removed")] int Removed)
    {
        public static RemovalProgress? Read(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<RemovalProgress>(payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
