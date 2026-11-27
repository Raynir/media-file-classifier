using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.Review;

/// <summary>
/// Gets the open duplicate groups for the review list (E12-S03-T01).
/// </summary>
/// <param name="Sort">The order.</param>
/// <param name="Filter">The types to show.</param>
public sealed record GetDuplicateGroupsQuery(DuplicateGroupSort Sort = DuplicateGroupSort.WastedSpace, DuplicateTypeFilter Filter = DuplicateTypeFilter.All)
    : IRequest<DuplicateGroupList>;

/// <summary>
/// Handles <see cref="GetDuplicateGroupsQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetDuplicateGroupsQueryHandler(ILibrarySession session) : IRequestHandler<GetDuplicateGroupsQuery, DuplicateGroupList>
{
    /// <inheritdoc />
    public async Task<DuplicateGroupList> Handle(GetDuplicateGroupsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return DuplicateGroupList.Create([], request.Sort, request.Filter);
        }

        var open = await library.Duplicates.GetSummariesAsync(DuplicateGroupState.Open, cancellationToken).ConfigureAwait(false);
        return DuplicateGroupList.Create(open, request.Sort, request.Filter);
    }
}
