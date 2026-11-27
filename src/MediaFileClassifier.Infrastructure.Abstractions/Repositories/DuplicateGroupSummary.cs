using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// A duplicate group with the figures the review list shows and sorts by (E12-S03).
/// </summary>
/// <param name="Id">Group ID.</param>
/// <param name="Type">How the members were found.</param>
/// <param name="MemberCount">Number of members.</param>
/// <param name="WastedBytes">The space that keeping only the largest member would free: the members' total size minus the largest.</param>
/// <param name="CreatedAt">When the group was found.</param>
public sealed record DuplicateGroupSummary(Id<DuplicateGroup> Id, DuplicateGroupType Type, int MemberCount, long WastedBytes, DateTimeOffset CreatedAt);
