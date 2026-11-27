using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Duplicates.Review;

namespace MediaFileClassifier.UseCases.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="DuplicateGroupList"/>: the filter, the sort orders and the totals of the review list (E12-S03-T01).
/// </summary>
public sealed class DuplicateGroupListTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ByDefaultOrder_ListsTheMostWastedSpaceFirst()
    {
        var small = Group(DuplicateGroupType.Exact, 2, 100);
        var big = Group(DuplicateGroupType.Exact, 2, 9000);
        var medium = Group(DuplicateGroupType.Exact, 5, 500);

        var list = DuplicateGroupList.Create([small, big, medium], DuplicateGroupSort.WastedSpace, DuplicateTypeFilter.All);

        Assert.Equal([big, medium, small], list.Groups);
    }

    [Fact]
    public void Create_ByGroupSize_ListsTheLargestGroupFirstThenTheMostWasted()
    {
        var pair = Group(DuplicateGroupType.Exact, 2, 9000);
        var fiveSmall = Group(DuplicateGroupType.Exact, 5, 10);
        var fiveBig = Group(DuplicateGroupType.Exact, 5, 20);

        var list = DuplicateGroupList.Create([pair, fiveSmall, fiveBig], DuplicateGroupSort.GroupSize, DuplicateTypeFilter.All);

        Assert.Equal([fiveBig, fiveSmall, pair], list.Groups);
    }

    [Fact]
    public void Create_ByType_ListsTheTypesTogetherWithTheMostWastedFirstInEach()
    {
        var exactSmall = Group(DuplicateGroupType.Exact, 2, 1);
        var exactBig = Group(DuplicateGroupType.Exact, 2, 5);

        var list = DuplicateGroupList.Create([exactSmall, exactBig], DuplicateGroupSort.Type, DuplicateTypeFilter.All);

        Assert.Equal([exactBig, exactSmall], list.Groups);
    }

    [Theory]
    [InlineData(DuplicateTypeFilter.All, 1)]
    [InlineData(DuplicateTypeFilter.Exact, 1)]
    [InlineData(DuplicateTypeFilter.Similar, 0)]
    public void Create_WithAFilter_KeepsOnlyThatType(DuplicateTypeFilter filter, int expected)
    {
        var list = DuplicateGroupList.Create([Group(DuplicateGroupType.Exact, 2, 1)], DuplicateGroupSort.WastedSpace, filter);

        Assert.Equal(expected, list.Groups.Count);
    }

    [Fact]
    public void Create_TotalsCoverEveryOpenGroupWhateverTheFilter()
    {
        var list = DuplicateGroupList.Create(
            [Group(DuplicateGroupType.Exact, 2, 100), Group(DuplicateGroupType.Exact, 3, 250)],
            DuplicateGroupSort.WastedSpace,
            DuplicateTypeFilter.Similar);

        Assert.Empty(list.Groups);
        Assert.Equal(2, list.TotalGroups);
        Assert.Equal(350, list.TotalWastedBytes);
    }

    [Fact]
    public void Create_WithoutGroups_IsEmptyWithZeroTotals()
    {
        var list = DuplicateGroupList.Create([], DuplicateGroupSort.WastedSpace, DuplicateTypeFilter.All);

        Assert.Empty(list.Groups);
        Assert.Equal(0, list.TotalGroups);
        Assert.Equal(0, list.TotalWastedBytes);
    }

    private static DuplicateGroupSummary Group(DuplicateGroupType type, int members, long wasted) =>
        new(Id<DuplicateGroup>.New(), type, members, wasted, Start);
}
