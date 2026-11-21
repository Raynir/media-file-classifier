using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Exclusions.GetExcludedItems;

/// <summary>
/// Lists the items the user removed from the open library (E02-S10-T04), newest removal first. Items excluded by their
/// folder's rules aren't listed; Manage folders handles them (D187).
/// </summary>
public sealed record GetExcludedItemsQuery : IRequest<IReadOnlyList<ExcludedItem>>;

/// <summary>
/// An item the user removed from the library.
/// </summary>
/// <param name="Id">The item.</param>
/// <param name="FileName">The file name.</param>
/// <param name="FolderPath">Where the file is: its folder's display path and the directory inside it.</param>
/// <param name="RemovedAt">When it was removed.</param>
public sealed record ExcludedItem(Id<MediaItem> Id, string FileName, string FolderPath, DateTimeOffset RemovedAt);

/// <summary>
/// Handles <see cref="GetExcludedItemsQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetExcludedItemsQueryHandler(ILibrarySession session) : IRequestHandler<GetExcludedItemsQuery, IReadOnlyList<ExcludedItem>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ExcludedItem>> Handle(GetExcludedItemsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        var folders = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(folder => folder.Id);
        var items = await library.Items.GetByStatusAsync(MediaItemStatus.Excluded, cancellationToken: cancellationToken).ConfigureAwait(false);
        return [.. items
            .Where(LibraryExclusions.IsListedAsRemoved)
            .OrderByDescending(item => item.StatusChangedAt)
            .ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ExcludedItem(item.Id, item.FileName, FolderPath(folders.GetValueOrDefault(item.FolderId)?.DisplayPath, item.Path.Value), item.StatusChangedAt))];
    }

    // The folder's display path joined with the file's directory inside it, in the folder path's own separator.
    private static string FolderPath(string? folder, string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : relativePath[..slash];
        if (folder is null)
        {
            return directory;
        }

        if (directory.Length == 0)
        {
            return folder;
        }

        var separator = folder.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        return folder.TrimEnd('\\', '/') + separator + directory.Replace('/', separator);
    }
}
