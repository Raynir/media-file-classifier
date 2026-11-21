using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Merges indexed folders into a folder that contains them (E02-S03-T02), for Add folder and for Index whole folder
/// (E03-S05): their items move to it with their IDs and metadata.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Cancels the merged folders' work, if the platform has background processing.</param>
/// <param name="hashJobs">Queues the full hashes the cancelled work didn't make.</param>
public sealed class FolderMerging(ILogger logger, IJobSystem? jobs, FullHashJobs? hashJobs)
{
    /// <summary>
    /// Merges every indexed folder inside a folder into it.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="files">Compares folder handles.</param>
    /// <param name="parent">The folder, already stored.</param>
    /// <param name="indexed">The library's other folders.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folders merged.</returns>
    public async Task<IReadOnlyList<IndexedFolder>> MergeContainedAsync(
        IOpenLibrary library,
        IMediaFileSource files,
        IndexedFolder parent,
        IReadOnlyList<IndexedFolder> indexed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(parent);

        var handle = parent.ToHandle();
        var merged = new List<IndexedFolder>();
        foreach (var child in indexed.Where(folder => folder.Id != parent.Id))
        {
            if (files.GetPathInside(child.ToHandle(), handle) is { Length: > 0 } relativePath)
            {
                await MergeAsync(library, parent, child, relativePath, cancellationToken).ConfigureAwait(false);
                merged.Add(child);
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges a folder into a folder that contains it.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="parent">The containing folder, already stored.</param>
    /// <param name="child">The folder inside it.</param>
    /// <param name="relativePath">The child's path relative to the parent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it's merged.</returns>
    public async Task MergeAsync(IOpenLibrary library, IndexedFolder parent, IndexedFolder child, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);

        // The child's jobs would write to a folder that's gone.
        await CancelJobsAsync(library, child, cancellationToken).ConfigureAwait(false);

        // Its excluded subfolders stay excluded. Its other rules give way to the parent's.
        parent.ChangeRules(parent.Rules with { ExcludePatterns = [.. parent.Rules.ExcludePatterns, .. child.Rules.ExcludePatternsUnder(relativePath)] });
        var moved = await library.Folders.MergeIntoAsync(parent, child.Id, relativePath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Merged {Child} into {Parent}: {Count} items moved", child.DisplayPath, parent.DisplayPath, moved);
        await QueueFullHashesAsync(library, parent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a folder's unfinished jobs.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when they're cancelled.</returns>
    public async Task CancelJobsAsync(IOpenLibrary library, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folder);

        if (jobs?.Current is not { } scheduler)
        {
            return;
        }

        foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false)).Where(job => job.FolderId == folder.Id))
        {
            await scheduler.CancelJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Queues the full hashes a folder's items lack. Its import queues the items still Discovered; the full hashes a
    /// cancelled job didn't make are queued here.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when they're queued.</returns>
    public async Task QueueFullHashesAsync(IOpenLibrary library, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folder);

        if (jobs?.Current is { } scheduler && hashJobs is not null)
        {
            var unhashed = (await library.Items.GetByFolderAsync(folder.Id, cancellationToken).ConfigureAwait(false))
                .Where(item => item.Status is MediaItemStatus.Pending or MediaItemStatus.Indexed && item.Hashes.ContentHash is null)
                .Select(item => item.Id)
                .ToList();
            if (unhashed.Count > 0)
            {
                await hashJobs.EnqueueAsync(scheduler, folder, unhashed, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
