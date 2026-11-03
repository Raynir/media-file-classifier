using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.DataAccess.Database;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for the media browser's reads and change event on <c>MediaItemRepository</c> (E14-S01-T02), and the
/// remembered tile size in the library settings (E14-S01-T05).
/// </summary>
public sealed class MediaItemSummaryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task GetSummariesAsync_PagesNewestFirst()
    {
        var items = Enumerable.Range(0, 30).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);

        var page = await bed.Library.Items.GetSummariesAsync(10, 5, Token);

        var expected = items.Select(item => item.Id.ToString()).Order(StringComparer.Ordinal).Reverse().Skip(10).Take(5);
        Assert.Equal(expected, page.Select(item => item.Id.ToString()));
    }

    [Fact]
    public async Task GetSummariesAsync_ReadsTheTileFactsWithDurationAndThumbnailFailure()
    {
        var plain = bed.NewItem(1);
        var video = MediaItem.Discover(bed.Folder.Id, RelativePath.From("clips/trip.MP4"), MediaType.Video, 2048, LibraryTestBed.Start, LibraryTestBed.Start, null, LibraryTestBed.Start);
        video.ChangeStatus(MediaItemStatus.Pending, LibraryTestBed.Start);
        video.ChangeStatus(MediaItemStatus.Error, LibraryTestBed.Start, "Decoder failed");
        await bed.Library.Items.AddAsync(plain, Token);
        await bed.Library.Items.AddAsync(video, Token);
        await using (var db = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(tracking: true, Token))
        {
            db.Add(new TechnicalMetadata
            {
                ItemId = video.Id,
                ExtractedAt = LibraryTestBed.Start,
                Duration = TimeSpan.FromSeconds(42),
                Warnings = [new ExtractionWarning(ThumbnailWarnings.ThumbnailFfmpegUnavailable)],
            });
            await db.SaveChangesAsync(Token);
        }

        var page = await bed.Library.Items.GetSummariesAsync(0, 10, Token);

        // UUIDv7 IDs made in the same millisecond sort at random, so look the items up.
        Assert.Equal(2, page.Count);
        var videoSummary = page.Single(item => item.Id == video.Id);
        var plainSummary = page.Single(item => item.Id == plain.Id);
        Assert.Equal(("trip.MP4", "mp4", MediaType.Video, 2048L), (videoSummary.FileName, videoSummary.FileFormat, videoSummary.MediaType, videoSummary.SizeBytes));
        Assert.Equal((MediaItemStatus.Error, "Decoder failed"), (videoSummary.Status, videoSummary.StatusReason));
        Assert.Equal(TimeSpan.FromSeconds(42), videoSummary.Duration);
        Assert.True(videoSummary.ThumbnailFailed);
        Assert.Null(plainSummary.Duration);
        Assert.False(plainSummary.ThumbnailFailed);
    }

    [Fact]
    public async Task CountNewerAsync_IsThePositionInNewestFirstOrder()
    {
        var items = Enumerable.Range(0, 10).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        var page = await bed.Library.Items.GetSummariesAsync(0, 10, Token);

        Assert.Equal(Enumerable.Range(0, 10), await Task.WhenAll(page.Select(item => bed.Library.Items.CountNewerAsync(item.Id, Token))));
    }

    [Fact]
    public async Task Changed_IsRaisedAfterEachWrite()
    {
        var raised = 0;
        bed.Library.Items.Changed += (_, _) => raised++;
        var item = bed.NewItem(1);

        await bed.Library.Items.AddAsync(item, Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(2, 150).Select(bed.NewItem)], Token);
        item.ChangeStatus(MediaItemStatus.Pending, bed.Clock.Tick());
        await bed.Library.Items.UpdateAsync(item, Token);
        await bed.Library.Items.DeleteAsync(item.Id, Token);
        await bed.Library.Items.DeleteAsync(item.Id, Token);

        // One add, two batches of 100, one update, one delete; deleting a missing item changes nothing.
        Assert.Equal(5, raised);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(288)]
    public async Task ThumbnailTileSize_IsStoredInTheLibrarySettings(int size)
    {
        var library = bed.Library.Library;
        library.ChangeSettings(library.Settings with { ThumbnailTileSize = size }, bed.Clock.Tick());
        await bed.Library.UpdateLibraryAsync(library, Token);

        await bed.ReopenAsync();

        Assert.Equal(size, bed.Library.Library.Settings.ThumbnailTileSize);
    }

    [Fact]
    public async Task ThumbnailTileSize_StoredBeforeTheSetting_ReadsAsTheDefault()
    {
        await using (var db = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(tracking: true, Token))
        {
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                db.Database,
                "UPDATE library SET settings = '{{\"analyzeAutomatically\":true,\"activeSearchModelId\":null,\"retainedPreviousRuns\":1}}'",
                Token);
        }

        await bed.ReopenAsync();

        Assert.Equal(LibrarySettings.DefaultThumbnailTileSize, bed.Library.Library.Settings.ThumbnailTileSize);
    }
}
