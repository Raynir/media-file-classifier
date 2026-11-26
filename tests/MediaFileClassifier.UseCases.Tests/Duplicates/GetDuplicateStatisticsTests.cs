using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Duplicates.GetDuplicateStatistics;
using MediaFileClassifier.UseCases.Tests.Libraries;

namespace MediaFileClassifier.UseCases.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="GetDuplicateStatisticsQueryHandler"/> (E12-S05-T01). The figures themselves are tested against
/// a real library in the Infrastructure tests.
/// </summary>
public sealed class GetDuplicateStatisticsTests
{
    [Fact]
    public async Task Handle_WithoutALibrary_ReturnsNoGroups()
    {
        var handler = new GetDuplicateStatisticsQueryHandler(new FakeLibrarySession());

        Assert.Equal(DuplicateStatistics.None, await handler.Handle(new GetDuplicateStatisticsQuery(), TestContext.Current.CancellationToken));
    }
}
