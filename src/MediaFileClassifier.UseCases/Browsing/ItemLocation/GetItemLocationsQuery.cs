using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.ItemLocation;

/// <summary>
/// Whether an item's file can be used, e.g. opened or revealed (E14-S04, D025).
/// </summary>
public enum ItemAvailability
{
    /// <summary>The file should be there.</summary>
    Available,

    /// <summary>The file was missing from its folder at the last scan.</summary>
    Missing,

    /// <summary>The item's folder is offline, e.g. an unplugged drive.</summary>
    Offline,
}

/// <summary>
/// Where an item's file is (E14-S04).
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Location">The file, for the platform services.</param>
/// <param name="FullPath">The file's full path, or <see langword="null"/> where folders aren't paths (Android).</param>
/// <param name="FolderName">The folder's name as shown to the user.</param>
/// <param name="Availability">Whether the file can be used.</param>
public sealed record ItemLocation(Id<MediaItem> ItemId, FileLocation Location, string? FullPath, string FolderName, ItemAvailability Availability);

/// <summary>
/// Gets where items' files are (E14-S04), in the order asked. Items that no longer exist are left out.
/// </summary>
/// <param name="ItemIds">The items.</param>
public sealed record GetItemLocationsQuery(IReadOnlyList<Id<MediaItem>> ItemIds) : IRequest<IReadOnlyList<ItemLocation>>;

/// <summary>
/// Handles <see cref="GetItemLocationsQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemLocationsQueryHandler(ILibrarySession session) : IRequestHandler<GetItemLocationsQuery, IReadOnlyList<ItemLocation>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemLocation>> Handle(GetItemLocationsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0)
        {
            return [];
        }

        var paths = (await library.Items.GetPathsAsync(request.ItemIds, cancellationToken).ConfigureAwait(false)).ToDictionary(path => path.Id);
        var folders = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(folder => folder.Id);
        var locations = new List<ItemLocation>(paths.Count);
        foreach (var id in request.ItemIds)
        {
            if (!paths.TryGetValue(id, out var path) || !folders.TryGetValue(path.FolderId, out var folder))
            {
                continue;
            }

            var relative = path.Path.Value;
            var fullPath = Path.IsPathFullyQualified(folder.PlatformHandle)
                ? Path.Combine(folder.PlatformHandle, relative.Replace('/', Path.DirectorySeparatorChar))
                : null;
            var availability = folder.Status == FolderStatus.Offline ? ItemAvailability.Offline
                : path.Status == MediaItemStatus.Missing ? ItemAvailability.Missing
                : ItemAvailability.Available;
            locations.Add(new ItemLocation(
                id, new FileLocation(new FolderHandle(folder.PlatformHandle, folder.DisplayPath), relative), fullPath, folder.DisplayPath, availability));
        }

        return locations;
    }
}
