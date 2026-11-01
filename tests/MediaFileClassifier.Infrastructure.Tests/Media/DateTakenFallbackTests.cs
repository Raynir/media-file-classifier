using System.Text;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Testing.Media;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests for each level of the date-taken chain: EXIF DateTimeOriginal, video creation time, file modification time.
/// </summary>
public sealed class DateTakenFallbackTests
{
    private static readonly DateTimeOffset Modified = new(2025, 3, 4, 5, 6, 7, 890, TimeSpan.FromHours(7));

    [Fact]
    public async Task ExifDate_WithoutOffset_IsLocalTimeWithUnknownOffset()
    {
        var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(SampleMedia.JpegWithExif, Modified);

        Assert.Equal(new DateTime(2026, 10, 29, 12, 34, 56), metadata.DateTaken);
        Assert.Equal(DateTimeKind.Unspecified, metadata.DateTaken!.Value.Kind);
        Assert.Null(metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.ExifDateTimeOriginal, metadata.DateTakenSource);
    }

    [Fact]
    public async Task ExifDate_WithOffsetTimeOriginal_KeepsTheOffset()
    {
        var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(SampleMedia.JpegWithIccAndOffset, Modified);

        Assert.Equal(new DateTime(2026, 6, 15, 8, 30, 0, 250), metadata.DateTaken);
        Assert.Equal(TimeSpan.FromHours(3), metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.ExifDateTimeOriginal, metadata.DateTakenSource);
    }

    [Fact]
    public async Task InvalidExifDate_FallsBackToModificationTimeWithWarning()
    {
        // Cameras with an unset clock write zeros.
        var bytes = await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, TestContext.Current.CancellationToken);
        var path = Path.Combine(Path.GetTempPath(), $"mfc-tests-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, Replace(bytes, "2026:10:29 12:34:56", "0000:00:00 00:00:00"), TestContext.Current.CancellationToken);
        try
        {
            var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(path, Modified);

            Assert.Equal(DateTakenSource.FileModified, metadata.DateTakenSource);
            Assert.Contains(new ExtractionWarning(MetadataWarnings.InvalidDateTaken, "0000:00:00 00:00:00"), metadata.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VideoCreationTime_IsUtcWithZeroOffset()
    {
        var metadata = await VideoMetadataExtractorTests.ExtractWithFfprobeAsync(SampleMedia.PortraitVideo);

        Assert.Equal(new DateTime(2026, 7, 1, 10, 20, 30), metadata.DateTaken);
        Assert.Equal(TimeSpan.Zero, metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.VideoCreationTime, metadata.DateTakenSource);
    }

    [Fact]
    public async Task VideoWithoutCreationTime_FallsBackToModificationTime()
    {
        var metadata = await VideoMetadataExtractorTests.ExtractWithFfprobeAsync(SampleMedia.HevcVideo, modified: Modified);

        AssertModificationTime(metadata);
    }

    [Fact]
    public async Task VideoWithoutFfmpeg_FallsBackToModificationTime()
    {
        await using var services = CreateServices();
        var detection = await DetectFileAsync(SampleMedia.PortraitVideo);
        var content = new MediaContent(Id<MediaItem>.New(), Modified, _ => Task.FromResult<Stream>(File.OpenRead(SampleMedia.PortraitVideo)));

        // The test services have Video analysis off, as on a machine without FFmpeg.
        var metadata = await detection.Handler!.MetadataExtractor.ExtractAsync(content, detection, TestContext.Current.CancellationToken);

        AssertModificationTime(metadata);
        Assert.Equal(MetadataWarnings.FfmpegUnavailable, Assert.Single(metadata.Warnings).Code);
    }

    [Theory]
    [InlineData("png-alpha.png")]
    [InlineData("gif-animated.gif")]
    [InlineData("webp.webp")]
    public async Task ImageWithoutExifDate_FallsBackToModificationTime(string sample)
    {
        var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(Path.Combine(SampleMedia.Directory, sample), Modified);

        AssertModificationTime(metadata);
    }

    private static void AssertModificationTime(TechnicalMetadata metadata)
    {
        Assert.Equal(new DateTime(2025, 3, 3, 22, 6, 7, 890), metadata.DateTaken);
        Assert.Equal(TimeSpan.Zero, metadata.DateTakenOffset);
        Assert.Equal(DateTakenSource.FileModified, metadata.DateTakenSource);
    }

    private static byte[] Replace(byte[] bytes, string oldText, string newText)
        => Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(bytes).Replace(oldText, newText, StringComparison.Ordinal));
}
