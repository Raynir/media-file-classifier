using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.ChangeFolderRules;

/// <summary>
/// Changes a folder's rules: excluded subfolders, hidden files and cloud placeholders (E02-S03-T03). Items under a
/// newly excluded path are marked Excluded (D048): user values are kept, thumbnails are dropped, files aren't touched.
/// Then the folder is discovered again, which brings back items that aren't excluded any more and imports new files.
/// </summary>
/// <param name="FolderId">The folder.</param>
/// <param name="Rules">Its new rules.</param>
public sealed record ChangeFolderRulesCommand(Id<IndexedFolder> FolderId, FolderRules Rules) : IRequest<ChangeFolderRulesResult>;

/// <summary>
/// What changing the rules did.
/// </summary>
/// <param name="Excluded">Items marked Excluded.</param>
/// <param name="Restored">Excluded items that the new rules include, which discovery queues to be imported again.</param>
public sealed record ChangeFolderRulesResult(int Excluded, int Restored);

/// <summary>
/// Handles <see cref="ChangeFolderRulesCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="thumbnails">Drops the excluded items' thumbnails.</param>
/// <param name="sender">Starts discovery again.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">Cancels the folder's running import, if the platform has background processing.</param>
public sealed class ChangeFolderRulesCommandHandler(
    ILibrarySession session,
    IThumbnailCache thumbnails,
    ISender sender,
    TimeProvider clock,
    ILogger<ChangeFolderRulesCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<ChangeFolderRulesCommand, ChangeFolderRulesResult>
{
    /// <inheritdoc />
    public async Task<ChangeFolderRulesResult> Handle(ChangeFolderRulesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var folder = await library.Folders.GetAsync(request.FolderId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("There's no such folder.", nameof(request));

        // A running import would go on with the old rules.
        if (jobs?.Current is { } scheduler)
        {
            foreach (var job in (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
                .Where(job => job.FolderId == folder.Id && job.Type == ImportStages.ImportJob))
            {
                await scheduler.CancelJobAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        folder.ChangeRules(request.Rules);
        await library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);

        var matcher = new FolderRuleMatcher(request.Rules);
        var now = clock.GetUtcNow();
        var excluded = new List<MediaItem>();
        var restored = 0;
        foreach (var item in await library.Items.GetByFolderAsync(folder.Id, cancellationToken).ConfigureAwait(false))
        {
            var included = matcher.IsIncluded(item.Path.Value);
            if (!included && item.Status != MediaItemStatus.Excluded)
            {
                item.ChangeStatus(MediaItemStatus.Excluded, now, IndexedFolders.ExcludedByRules);
                excluded.Add(item);
            }
            else if (IndexedFolders.IsBackInRules(item, matcher))
            {
                restored++;
            }
        }

        await library.Items.UpdateRangeAsync(excluded, cancellationToken).ConfigureAwait(false);
        foreach (var item in excluded)
        {
            thumbnails.Invalidate(library.Layout, item.Id);
        }

        logger.LogInformation("Changed the rules of {Folder}: {Excluded} items excluded, {Restored} to bring back", folder.DisplayPath, excluded.Count, restored);

        // Discovery queues the items brought back, and finds the files the old rules skipped.
        await sender.Send(new ImportFoldersCommand([folder.Id]), cancellationToken).ConfigureAwait(false);
        return new ChangeFolderRulesResult(excluded.Count, restored);
    }
}
