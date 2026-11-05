using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Rescan.RemoveMissingItems;

/// <summary>
/// Counts the items of the open library whose file is gone (E02-S04-T04), for the confirmation of
/// <see cref="RemoveMissingItemsCommand"/>. 0 when no library is open.
/// </summary>
public sealed record CountMissingItemsQuery : IRequest<int>;

/// <summary>
/// Handles <see cref="CountMissingItemsQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class CountMissingItemsQueryHandler(ILibrarySession session) : IRequestHandler<CountMissingItemsQuery, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(CountMissingItemsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return 0;
        }

        return await library.Items.CountAsync(new MediaItemFilter { Status = MediaItemStatus.Missing }, cancellationToken).ConfigureAwait(false);
    }
}
