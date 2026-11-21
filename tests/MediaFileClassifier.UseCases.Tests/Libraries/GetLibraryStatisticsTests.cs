using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for <see cref="GetLibraryStatisticsQueryHandler"/> (E02-S05-T01).
/// </summary>
public sealed class GetLibraryStatisticsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_WithoutALibrary_ReturnsNull()
    {
        var handler = new GetLibraryStatisticsQueryHandler(new FakeLibrarySession());

        Assert.Null(await handler.Handle(new GetLibraryStatisticsQuery(), Token));
    }

    [Fact]
    public async Task Handle_CountsTheOpenLibrarysItems()
    {
        var session = new FakeLibrarySession();
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "statistics"), "statistics");
        var now = DateTimeOffset.UnixEpoch;
        var video = MediaItem.Discover(default, RelativePath.From("a.mp4"), MediaType.Video, 50, now, now, 1, now);
        library.ItemList.Items.Add(video);
        await session.SetCurrentAsync(library);

        var statistics = await new GetLibraryStatisticsQueryHandler(session).Handle(new GetLibraryStatisticsQuery(), Token);

        Assert.Equal(LibraryStatistics.Empty with { Videos = 1, Pending = 1, MediaSizeBytes = 50 }, statistics);
    }

    [Theory]
    [InlineData(MediaItemStatus.Discovered, 0, 1, 0, 0, 10)]
    [InlineData(MediaItemStatus.Pending, 0, 1, 0, 0, 10)]
    [InlineData(MediaItemStatus.Indexed, 1, 0, 0, 0, 10)]
    [InlineData(MediaItemStatus.Error, 0, 0, 1, 0, 10)]
    [InlineData(MediaItemStatus.Missing, 0, 0, 0, 1, 0)]
    public void Summarize_SortsEachStatus(MediaItemStatus status, int indexed, int pending, int errors, int missing, long size)
    {
        var statistics = GetLibraryStatisticsQueryHandler.Summarize([new(MediaType.Image, status, 1, 10)]);

        Assert.Equal(new LibraryStatistics(1, 0, 0, indexed, pending, errors, missing, size), statistics);
    }

    [Fact]
    public void Summarize_LeavesOutExcludedItems()
    {
        var statistics = GetLibraryStatisticsQueryHandler.Summarize([new(MediaType.Image, MediaItemStatus.Excluded, 3, 10)]);

        Assert.Equal(LibraryStatistics.Empty, statistics);
    }
}
