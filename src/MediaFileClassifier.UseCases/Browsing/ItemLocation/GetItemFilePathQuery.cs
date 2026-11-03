using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.ItemLocation;

/// <summary>
/// Gets the full file path of an item of the open library, e.g. to open it in the default app until the preview
/// exists (E14-S01). <see langword="null"/> when the item or its folder is gone, or its folder has no file-system path
/// (Android folder handles, E20).
/// </summary>
/// <param name="ItemId">The item.</param>
public sealed record GetItemFilePathQuery(Id<MediaItem> ItemId) : IRequest<string?>;

/// <summary>
/// Handles <see cref="GetItemFilePathQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetItemFilePathQueryHandler(ILibrarySession session) : IRequestHandler<GetItemFilePathQuery, string?>
{
    /// <inheritdoc />
    public async Task<string?> Handle(GetItemFilePathQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library
            || await library.Items.GetAsync(request.ItemId, cancellationToken).ConfigureAwait(false) is not { } item
            || await library.Folders.GetAsync(item.FolderId, cancellationToken).ConfigureAwait(false) is not { } folder
            || !Path.IsPathFullyQualified(folder.PlatformHandle))
        {
            return null;
        }

        return Path.Combine(folder.PlatformHandle, item.Path.Value.Replace('/', Path.DirectorySeparatorChar));
    }
}
