using Avalonia;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the Technical tab's rows (E14-S05-T01) and the preview's orientation (E14-S05-T02).
/// </summary>
public sealed class TechnicalRowsTests
{
    private static readonly Id<MediaItem> ItemId = Id<MediaItem>.New();

    [Fact]
    public void Photo_ShowsFigmasRowsThenTheExtractedOnes_WithTheOrientationApplied()
    {
        var item = Item(MediaType.Image, new TechnicalMetadata
        {
            ItemId = ItemId,
            Width = 4000,
            Height = 3000,
            Orientation = ImageOrientation.Rotate90,
            DateTaken = new DateTime(2026, 7, 14, 18, 30, 0),
            DateTakenSource = DateTakenSource.ExifDateTimeOriginal,
            CameraMake = "Canon",
            CameraModel = "Canon EOS R5",
            LensModel = "RF 24-70mm",
            ColorSpace = "sRGB",
            Location = new GeoLocation(48.8566, 2.3522),
            ExtractedAt = DateTimeOffset.UnixEpoch,
        });

        var rows = Rows(item);

        Assert.Equal(
            ["Type", "Format", "Dimensions", "File size", "Status", "Date taken", "Camera", "Lens", "Color space", "Location", "Folder", "Path", "Imported"],
            rows.Keys);
        Assert.Equal("Image", rows["Type"]);
        Assert.Equal("JPG", rows["Format"]);
        Assert.Equal("3000 × 4000 px", rows["Dimensions"]);
        Assert.Equal("Indexed", rows["Status"]);
        Assert.EndsWith("(EXIF)", rows["Date taken"], StringComparison.Ordinal);
        Assert.Equal("Canon EOS R5", rows["Camera"]);
        Assert.Equal("48.85660, 2.35220", rows["Location"]);
        Assert.Equal("trip/photo.jpg", rows["Path"]);
    }

    [Fact]
    public void Video_ShowsDurationFrameRateAndCodecs()
    {
        var item = Item(MediaType.Video, new TechnicalMetadata
        {
            ItemId = ItemId,
            Width = 1920,
            Height = 1080,
            Duration = TimeSpan.FromSeconds(102),
            FrameRate = 29.97,
            VideoCodec = "h264",
            HasAudio = false,
            ExtractedAt = DateTimeOffset.UnixEpoch,
        });

        var rows = Rows(item);

        Assert.Equal("1:42", rows["Duration"]);
        Assert.Equal($"{Format.Number(29.97, 2)} fps", rows["Frame rate"]);
        Assert.Equal("h264 / no audio", rows["Codecs"]);
        Assert.DoesNotContain("Frames", rows.Keys);
    }

    [Fact]
    public void AnimatedImage_ShowsFramesAndLoops()
    {
        var item = Item(MediaType.AnimatedImage, new TechnicalMetadata { ItemId = ItemId, FrameCount = 12, LoopCount = 0, ExtractedAt = DateTimeOffset.UnixEpoch });

        var rows = Rows(item);

        Assert.Equal(("12", "Forever"), (rows["Frames"], rows["Loops"]));
        Assert.DoesNotContain("Dimensions", rows.Keys);
    }

    [Theory]
    [InlineData(ItemAvailability.Offline, MediaItemStatus.Indexed, "Offline: Photos")]
    [InlineData(ItemAvailability.Missing, MediaItemStatus.Missing, "Missing")]
    [InlineData(ItemAvailability.Available, MediaItemStatus.Error, "Error")]
    public void Status_SaysWhyTheFileCantBeUsed(ItemAvailability availability, MediaItemStatus status, string text)
    {
        var item = Item(MediaType.Image, technical: null, availability, status) with { StatusReason = "Damaged file" };

        var rows = Rows(item);

        Assert.Equal(text, rows["Status"]);
        Assert.Equal(status == MediaItemStatus.Error, rows.ContainsKey("Error"));
    }

    [Theory]
    [InlineData(ImageOrientation.Normal, 0, 0)]
    [InlineData(ImageOrientation.FlipHorizontal, 1, 0)]
    [InlineData(ImageOrientation.Rotate180, 1, 1)]
    [InlineData(ImageOrientation.FlipVertical, 0, 1)]
    [InlineData(ImageOrientation.Transpose, 0, 0)]
    [InlineData(ImageOrientation.Rotate90, 1, 0)]
    [InlineData(ImageOrientation.Transverse, 1, 1)]
    [InlineData(ImageOrientation.Rotate270, 0, 1)]
    public void Orientation_MovesTheStoredTopLeftCornerWhereExifSays(ImageOrientation orientation, double x, double y)
    {
        var corner = new Point(0, 0).Transform(ZoomImage.OrientationMatrix(orientation));

        Assert.Equal(new Point(x, y), corner);

        // The stored top edge runs down the right side after a 90° turn.
        if (orientation == ImageOrientation.Rotate90)
        {
            Assert.Equal(new Point(1, 1), new Point(1, 0).Transform(ZoomImage.OrientationMatrix(orientation)));
        }
    }

    private static Dictionary<string, string> Rows(ItemDetails item) =>
        TechnicalRows.Build(item, Localizer.Current).ToDictionary(row => row.Label, row => row.Value);

    private static ItemDetails Item(
        MediaType type, TechnicalMetadata? technical, ItemAvailability availability = ItemAvailability.Available, MediaItemStatus status = MediaItemStatus.Indexed) =>
        new(
            ItemId,
            "photo.jpg",
            "jpg",
            type,
            2_200_000,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            status,
            null,
            new ItemLocation(ItemId, new FileLocation(new FolderHandle(@"D:\Photos", "Photos"), "trip/photo.jpg"), @"D:\Photos\trip\photo.jpg", "Photos", availability),
            technical,
            null,
            null,
            []);
}
