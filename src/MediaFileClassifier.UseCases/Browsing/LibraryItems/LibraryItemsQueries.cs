using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.LibraryItems;

/// <summary>
/// Counts the items of the open library (E14-S01-T02). 0 when no library is open.
/// </summary>
public sealed record CountLibraryItemsQuery : IRequest<int>;

/// <summary>
/// Gets a page of the open library's items, newest first (E14-S01-T02). Empty when no library is open.
/// </summary>
/// <param name="Offset">Position of the first item.</param>
/// <param name="Limit">Maximum number of items.</param>
public sealed record GetLibraryItemsPageQuery(int Offset, int Limit) : IRequest<IReadOnlyList<MediaItemSummary>>;

/// <summary>
/// Finds an item's position in the newest-first order of <see cref="GetLibraryItemsPageQuery"/>.
/// </summary>
/// <param name="ItemId">The item.</param>
public sealed record GetLibraryItemPositionQuery(Id<MediaItem> ItemId) : IRequest<int>;

/// <summary>
/// Handles the library item queries of the media browser.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class LibraryItemsQueryHandlers(ILibrarySession session)
    : IRequestHandler<CountLibraryItemsQuery, int>,
      IRequestHandler<GetLibraryItemsPageQuery, IReadOnlyList<MediaItemSummary>>,
      IRequestHandler<GetLibraryItemPositionQuery, int>
{
    /// <inheritdoc />
    public Task<int> Handle(CountLibraryItemsQuery request, CancellationToken cancellationToken) =>
        session.Current is { } library ? library.Items.CountAsync(cancellationToken) : Task.FromResult(0);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemSummary>> Handle(GetLibraryItemsPageQuery request, CancellationToken cancellationToken) =>
        session.Current is { } library
            ? library.Items.GetSummariesAsync(request.Offset, request.Limit, cancellationToken)
            : Task.FromResult<IReadOnlyList<MediaItemSummary>>([]);

    /// <inheritdoc />
    public Task<int> Handle(GetLibraryItemPositionQuery request, CancellationToken cancellationToken) =>
        session.Current is { } library ? library.Items.CountNewerAsync(request.ItemId, cancellationToken) : Task.FromResult(0);
}
