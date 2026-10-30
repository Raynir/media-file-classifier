using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Tests for <see cref="TechnicalMetadata"/> and <see cref="GeoLocation"/>.
/// </summary>
public sealed class TechnicalMetadataTests
{
    [Theory]
    [InlineData(ImageOrientation.Normal, 4000, 3000)]
    [InlineData(ImageOrientation.Rotate180, 4000, 3000)]
    [InlineData(ImageOrientation.Rotate90, 3000, 4000)]
    [InlineData(ImageOrientation.Rotate270, 3000, 4000)]
    public void DisplaySize_AppliesOrientation(ImageOrientation orientation, int width, int height)
    {
        var metadata = Create() with { Width = 4000, Height = 3000, Orientation = orientation };

        Assert.Equal(width, metadata.DisplayWidth);
        Assert.Equal(height, metadata.DisplayHeight);
        Assert.Equal(12_000_000, metadata.PixelCount);
    }

    [Fact]
    public void PixelCount_IsNullWithoutDimensions()
    {
        Assert.Null(Create().PixelCount);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void GeoLocation_RejectsOutOfRangeCoordinates(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoLocation(latitude, longitude));
    }

    private static TechnicalMetadata Create() =>
        new() { ItemId = Id<MediaItem>.New(), ExtractedAt = DateTimeOffset.UnixEpoch };
}
