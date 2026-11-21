using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Exclusions.RestoreExcludedItems;

/// <summary>
/// Brings items the user removed back into the library (E02-S10-T04). An item whose file is gone becomes Missing, so a
/// rescan can relink it; one its folder's rules now exclude stays out as rule-excluded (D187); any other is imported
/// again, which makes its thumbnails and, as the library's settings say, its AI results again. Its identity, user
/// values and collections were kept, so they come back with it.
/// </summary>
/// <param name="ItemIds">The items. Items that aren't removed by the user are skipped.</param>
public sealed record RestoreExcludedItemsCommand(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<RestoreExcludedItemsResult>;

/// <summary>
/// What restoring the items did.
/// </summary>
/// <param name="Imported">Items queued to be imported again.</param>
/// <param name="Missing">Items whose file is gone, now Missing.</param>
/// <param name="ExcludedByRules">Items their folder's rules exclude, now rule-excluded.</param>
public sealed record RestoreExcludedItemsResult(int Imported, int Missing, int ExcludedByRules);

/// <summary>
/// Handles <see cref="RestoreExcludedItemsCommand"/>.
/// </summary>
/// <param name="jobs">Runs the import of the open library.</param>
/// <param name="files">Checks whether the files are still there.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class RestoreExcludedItemsCommandHandler(
    IJobSystem jobs,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<RestoreExcludedItemsCommandHandler> logger) : IRequestHandler<RestoreExcludedItemsCommand, RestoreExcludedItemsResult>
{
    /// <inheritdoc />
    public async Task<RestoreExcludedItemsResult> Handle(RestoreExcludedItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scheduler = jobs.Current ?? throw new InvalidOperationException("No library is open.");
        var library = scheduler.Library;
        var now = clock.GetUtcNow();
        var items = (await library.Items.GetByIdsAsync(request.ItemIds, cancellationToken).ConfigureAwait(false))
            .Where(LibraryExclusions.IsListedAsRemoved);
        var (imported, missing, ruled) = (0, 0, 0);
        foreach (var byFolder in items.GroupBy(item => item.FolderId))
        {
            var folder = await library.Folders.GetAsync(byFolder.Key, cancellationToken).ConfigureAwait(false);
            if (folder is null)
            {
                continue;
            }

            var rules = new FolderRuleMatcher(folder.Rules);
            var changed = new List<MediaItem>();
            var import = new List<Id<MediaItem>>();
            foreach (var item in byFolder)
            {
                var stat = await StatAsync(folder, item, cancellationToken).ConfigureAwait(false);
                if (stat is null)
                {
                    item.ChangeStatus(MediaItemStatus.Missing, now);
                    missing++;
                }
                else if (!rules.IsIncluded(item.Path.Value))
                {
                    item.ChangeExclusionReason(IndexedFolders.ExcludedByRules);
                    ruled++;
                }
                else
                {
                    // Changed while it was out of the library: what was known about the old content is stale.
                    if (stat.Length != item.SizeBytes || ToStoredPrecision(stat.LastModifiedUtc) != item.FileModifiedAt)
                    {
                        item.UpdateFile(stat.Length, stat.CreatedUtc ?? stat.LastModifiedUtc, stat.LastModifiedUtc, new FileHashes());
                    }

                    item.ChangeExclusionReason(LibraryExclusions.RestoreRequested);
                    import.Add(item.Id);
                    imported++;
                }

                changed.Add(item);
            }

            await library.Items.UpdateRangeAsync(changed, cancellationToken).ConfigureAwait(false);
            if (import.Count > 0)
            {
                var description = import.Count == 1 ? $"Restore 1 item in {folder.DisplayPath}" : $"Restore {import.Count} items in {folder.DisplayPath}";
                await scheduler.EnqueueJobAsync(
                    new JobRequest(ImportStages.ImportJob, description, JobPriority.UserAction, [.. import.Select(id => new WorkItemRequest(ImportStages.Fingerprint, id))])
                    {
                        FolderId = folder.Id,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Restored removed items: {Imported} imported again, {Missing} missing, {Ruled} excluded by their folder's rules", imported, missing, ruled);
        return new RestoreExcludedItemsResult(imported, missing, ruled);
    }

    // Times are stored as Unix milliseconds (D071), so the file's time is cut to match before comparing.
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset time) =>
        DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

    // An unreadable folder counts as the file being gone: Missing items are relinked when it comes back.
    private async Task<MediaFileStat?> StatAsync(IndexedFolder folder, MediaItem item, CancellationToken cancellationToken)
    {
        try
        {
            return await files.GetStatAsync(ItemStage.GetLocation(folder, item), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
