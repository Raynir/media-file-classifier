using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Media;

/// <summary>
/// Tests that rotated photos and videos report the dimensions they are shown at, for the resolution filters.
/// </summary>
public sealed class OrientationTests
{
    [Fact]
    public async Task RotatedPortraitPhoto_ReportsPortraitDimensions()
    {
        // Stored 640×480 with EXIF Orientation 6: shown turned 90° clockwise.
        var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(SampleMedia.JpegWithExif);

        Assert.Equal(ImageOrientation.Rotate90, metadata.Orientation);
        Assert.Equal((640, 480), (metadata.Width, metadata.Height));
        Assert.Equal((480, 640), (metadata.DisplayWidth, metadata.DisplayHeight));
    }

    [Fact]
    public async Task UnrotatedPhoto_ReportsStoredDimensions()
    {
        var metadata = await ImageMetadataExtractorTests.ExtractFileAsync(SampleMedia.JpegWithIccAndOffset);

        Assert.Equal((200, 100), (metadata.DisplayWidth, metadata.DisplayHeight));
    }

    [Fact]
    public async Task PortraitVideoStoredLandscape_ReportsPortraitDimensions()
    {
        var metadata = await VideoMetadataExtractorTests.ExtractWithFfprobeAsync(SampleMedia.PortraitVideo);

        Assert.Equal(ImageOrientation.Rotate90, metadata.Orientation);
        Assert.Equal((160, 90), (metadata.Width, metadata.Height));
        Assert.Equal((90, 160), (metadata.DisplayWidth, metadata.DisplayHeight));
    }

    [Theory]
    [InlineData(0, ImageOrientation.Normal)]
    [InlineData(-90, ImageOrientation.Rotate90)]
    [InlineData(270, ImageOrientation.Rotate90)]
    [InlineData(90, ImageOrientation.Rotate270)]
    [InlineData(-270, ImageOrientation.Rotate270)]
    [InlineData(180, ImageOrientation.Rotate180)]
    [InlineData(-180, ImageOrientation.Rotate180)]
    [InlineData(45, ImageOrientation.Normal)]
    [InlineData(360, ImageOrientation.Normal)]
    public void VideoRotation_MapsToOrientation(int counterClockwiseDegrees, ImageOrientation expected)
    {
        Assert.Equal(expected, VideoMetadataExtractor.ToOrientation(counterClockwiseDegrees));
    }
}
