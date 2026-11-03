using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.LibraryItems;

/// <summary>
/// The M1 result source (E14-S01-T02): every item of the open library, newest first. It follows the session, so it
/// reports a change when a library opens or closes and whenever the open library's items change.
/// </summary>
public sealed class LibraryItemsSource : IMediaResultSource
{
    private readonly ISender sender;
    private readonly ILibrarySession session;
    private readonly Lock syncRoot = new();
    private IMediaItemRepository? watched;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemsSource"/> class.
    /// </summary>
    /// <param name="sender">Sends the queries.</param>
    /// <param name="session">Holds the open library.</param>
    public LibraryItemsSource(ISender sender, ILibrarySession session)
    {
        this.sender = sender;
        this.session = session;
        session.CurrentChanged += (_, _) => Watch();
        Watch();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken) => sender.Send(new CountLibraryItemsQuery(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItemSummary>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken) =>
        sender.Send(new GetLibraryItemsPageQuery(offset, limit), cancellationToken);

    /// <inheritdoc />
    public Task<int> PositionOfAsync(Id<MediaItem> itemId, CancellationToken cancellationToken) =>
        sender.Send(new GetLibraryItemPositionQuery(itemId), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken) =>
        sender.Send(new GetLibraryItemIdsQuery(offset, limit, newest), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken) =>
        sender.Send(new GetExistingLibraryItemsQuery(ids), cancellationToken);

    private void Watch()
    {
        lock (syncRoot)
        {
            var items = session.Current?.Items;
            if (ReferenceEquals(items, watched))
            {
                return;
            }

            if (watched is not null)
            {
                watched.Changed -= OnItemsChanged;
            }

            watched = items;
            if (watched is not null)
            {
                watched.Changed += OnItemsChanged;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemsChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
