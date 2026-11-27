using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Duplicates.Review;

/// <summary>
/// How the review list is ordered (E12-S03-T01). Wasted space is the default, largest first.
/// </summary>
public enum DuplicateGroupSort
{
    /// <summary>
    /// The space freed by keeping one member, largest first.
    /// </summary>
    WastedSpace,

    /// <summary>
    /// By group type (Exact before Similar), then wasted space.
    /// </summary>
    Type,

    /// <summary>
    /// By number of members, largest first, then wasted space.
    /// </summary>
    GroupSize,
}

/// <summary>
/// Which groups the review list shows.
/// </summary>
public enum DuplicateTypeFilter
{
    /// <summary>
    /// Every type.
    /// </summary>
    All,

    /// <summary>
    /// Exact copies.
    /// </summary>
    Exact,

    /// <summary>
    /// Groups of similar items: every type that isn't exact.
    /// </summary>
    Similar,
}

/// <summary>
/// The open groups to review, filtered and sorted, with the totals of all open groups for the view's header.
/// </summary>
/// <param name="Groups">The groups to show.</param>
/// <param name="TotalGroups">All open groups, whatever the filter.</param>
/// <param name="TotalWastedBytes">The wasted space of all open groups.</param>
public sealed record DuplicateGroupList(IReadOnlyList<DuplicateGroupSummary> Groups, int TotalGroups, long TotalWastedBytes)
{
    /// <summary>
    /// Gets the list for a set of open groups.
    /// </summary>
    /// <param name="open">All open groups.</param>
    /// <param name="sort">The order.</param>
    /// <param name="filter">The types to keep.</param>
    /// <returns>The list.</returns>
    public static DuplicateGroupList Create(IReadOnlyList<DuplicateGroupSummary> open, DuplicateGroupSort sort, DuplicateTypeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(open);

        var shown = open.Where(group => filter switch
        {
            DuplicateTypeFilter.Exact => group.Type == DuplicateGroupType.Exact,
            DuplicateTypeFilter.Similar => group.Type != DuplicateGroupType.Exact,
            _ => true,
        });
        IOrderedEnumerable<DuplicateGroupSummary> ordered = sort switch
        {
            DuplicateGroupSort.Type => shown.OrderBy(group => group.Type).ThenByDescending(group => group.WastedBytes),
            DuplicateGroupSort.GroupSize => shown.OrderByDescending(group => group.MemberCount).ThenByDescending(group => group.WastedBytes),
            _ => shown.OrderByDescending(group => group.WastedBytes),
        };
        return new([.. ordered.ThenBy(group => group.CreatedAt).ThenBy(group => group.Id)], open.Count, open.Sum(group => group.WastedBytes));
    }
}
