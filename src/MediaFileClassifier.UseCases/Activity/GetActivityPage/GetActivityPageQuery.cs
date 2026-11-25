using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Activity.GetActivityPage;

/// <summary>
/// Gets one page of the activity log, newest first (E15-S04-T02).
/// </summary>
/// <param name="Filter">Which events to list.</param>
/// <param name="Skip">How many of the newest matching events to skip.</param>
/// <param name="Take">The page size.</param>
public sealed record GetActivityPageQuery(ActivityFilter Filter, int Skip, int Take) : IRequest<ActivityPage>;

/// <summary>
/// A page of the activity log.
/// </summary>
/// <param name="Entries">The events of the page, newest first.</param>
/// <param name="Total">How many events match the filter in all.</param>
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, int Total)
{
    /// <summary>
    /// Gets a page without events.
    /// </summary>
    public static ActivityPage Empty { get; } = new([], 0);
}

/// <summary>
/// An event, with the folder its "Show items" link leads to.
/// </summary>
/// <param name="Event">The event.</param>
/// <param name="ShowFolderId">The folder to show, or <see langword="null"/> when the event names none or the folder or item is gone.</param>
public sealed record ActivityEntry(ActivityEvent Event, Id<IndexedFolder>? ShowFolderId);

/// <summary>
/// Handles <see cref="GetActivityPageQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetActivityPageQueryHandler(ILibrarySession session) : IRequestHandler<GetActivityPageQuery, ActivityPage>
{
    /// <inheritdoc />
    public async Task<ActivityPage> Handle(GetActivityPageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return ActivityPage.Empty;
        }

        var (events, total) = await library.Activity.GetPageWithTotalAsync(request.Filter, Math.Max(0, request.Skip), Math.Max(1, request.Take), cancellationToken).ConfigureAwait(false);
        if (events.Count == 0)
        {
            return new ActivityPage([], total);
        }

        // A link needs the folder (or the item's folder) to still be there.
        var folderIds = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false)).Select(folder => folder.Id).ToHashSet();
        var itemIds = events.Where(activity => activity.ItemId is not null).Select(activity => activity.ItemId!.Value).Distinct().ToList();
        var itemFolders = itemIds.Count == 0
            ? []
            : (await library.Items.GetPathsAsync(itemIds, cancellationToken).ConfigureAwait(false)).ToDictionary(path => path.Id, path => path.FolderId);

        var entries = events.Select(activity =>
        {
            Id<IndexedFolder>? folder = null;
            if (activity.ItemId is { } itemId)
            {
                // An event about one item links to that item's folder, and to nothing once the item is gone.
                if (itemFolders.TryGetValue(itemId, out var itemFolder) && folderIds.Contains(itemFolder))
                {
                    folder = itemFolder;
                }
            }
            else if (activity.FolderId is { } folderId && folderIds.Contains(folderId))
            {
                folder = folderId;
            }

            return new ActivityEntry(activity, folder);
        }).ToList();
        return new ActivityPage(entries, total);
    }
}
