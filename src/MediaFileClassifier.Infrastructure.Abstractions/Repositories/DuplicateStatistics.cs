namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The figures of a library's Open duplicate groups (E12-S05-T01).
/// </summary>
/// <param name="OpenGroups">The number of Open groups, of every type.</param>
/// <param name="WastedBytes">The space the extra copies take: per Open group the summed size of its members minus its largest member, summed over the groups.</param>
public sealed record DuplicateStatistics(int OpenGroups, long WastedBytes)
{
    /// <summary>
    /// Gets the figures of a library without Open groups.
    /// </summary>
    public static DuplicateStatistics None { get; } = new(0, 0);
}
