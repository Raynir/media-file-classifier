using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;

/// <summary>
/// Tests for <see cref="OrientationTransform"/>: every EXIF orientation turns the stored pixels upright.
/// </summary>
public sealed class OrientationTransformTests
{
    /// <summary>
    /// Where each stored corner of a 3×2 picture is shown, per the EXIF definitions: the corner shown at the top left,
    /// top right, bottom left and bottom right.
    /// </summary>
    public static TheoryData<ImageOrientation, string> Orientations => new()
    {
        { ImageOrientation.Normal, "ABCD" },
        { ImageOrientation.FlipHorizontal, "BADC" },
        { ImageOrientation.Rotate180, "DCBA" },
        { ImageOrientation.FlipVertical, "CDAB" },
        { ImageOrientation.Transpose, "ACBD" },
        { ImageOrientation.Rotate90, "CADB" },
        { ImageOrientation.Transverse, "DBCA" },
        { ImageOrientation.Rotate270, "BDAC" },
    };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Apply_PutsEachCornerWhereItIsShown(ImageOrientation orientation, string shownCorners)
    {
        // Stored corners: A top left, B top right, C bottom left, D bottom right.
        var colors = new Dictionary<char, SKColor> { ['A'] = SKColors.Red, ['B'] = SKColors.Lime, ['C'] = SKColors.Blue, ['D'] = SKColors.Yellow };
        var stored = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        stored.Erase(SKColors.Gray);
        stored.SetPixel(0, 0, colors['A']);
        stored.SetPixel(2, 0, colors['B']);
        stored.SetPixel(0, 1, colors['C']);
        stored.SetPixel(2, 1, colors['D']);

        using var upright = OrientationTransform.Apply(stored, orientation);

        var right = upright.Width - 1;
        var bottom = upright.Height - 1;
        Assert.Equal(orientation >= ImageOrientation.Transpose ? (2, 3) : (3, 2), (upright.Width, upright.Height));
        Assert.Equal(
            shownCorners.Select(corner => colors[corner]),
            [upright.GetPixel(0, 0), upright.GetPixel(right, 0), upright.GetPixel(0, bottom), upright.GetPixel(right, bottom)]);
    }
}
