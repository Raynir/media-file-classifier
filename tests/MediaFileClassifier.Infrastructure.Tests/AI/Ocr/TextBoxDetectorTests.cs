using System.Numerics;
using MediaFileClassifier.Infrastructure.AI.Ocr;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Ocr;

/// <summary>
/// Tests for <see cref="TextBoxDetector"/>, the DB post-processing of the in-process OCR.
/// </summary>
public sealed class TextBoxDetectorTests
{
    private static readonly TextDetectionOptions Options = new();

    [Fact]
    public void FindBoxes_OneBlock_GrowsItByTheUnclipDistance()
    {
        // A 40 x 10 block of text pixels at (20, 30).
        var map = Map(100, 80, (20, 30, 40, 10, 0.9f));

        var box = Assert.Single(TextBoxDetector.FindBoxes(map, 100, 80, Options));

        // The fitted rectangle spans pixel centres 20..60 x 30..40 (the 2 x 2 dilation adds one pixel right and down):
        // 40 x 10, grown by 40 * 10 * 1.6 / (2 * 50) = 6.4 on every side. Its score is the map's mean over the
        // 41 x 11 pixels it covers, 400 of them text.
        Assert.Equal(0.9f * 400 / 451, box.Score, 3);
        AssertCorner(new Vector2(13.6f, 23.6f), box.Corners[0]);
        AssertCorner(new Vector2(66.4f, 23.6f), box.Corners[1]);
        AssertCorner(new Vector2(66.4f, 46.4f), box.Corners[2]);
        AssertCorner(new Vector2(13.6f, 46.4f), box.Corners[3]);
    }

    [Fact]
    public void FindBoxes_TwoBlocks_FindsBoth()
    {
        var map = Map(200, 100, (10, 10, 50, 12, 0.8f), (100, 60, 80, 15, 0.8f));

        var boxes = TextBoxDetector.FindBoxes(map, 200, 100, Options);

        Assert.Equal(2, boxes.Count);
    }

    [Theory]
    [InlineData(0.29f)]
    [InlineData(0.45f)]
    public void FindBoxes_WeakOrMissingText_FindsNothing(float probability)
    {
        // Under the binary threshold nothing is text; between it and the box threshold, the box's mean is too low.
        var map = Map(100, 80, (20, 30, 40, 10, probability));

        Assert.Empty(TextBoxDetector.FindBoxes(map, 100, 80, Options));
    }

    [Fact]
    public void FindBoxes_SpeckOfNoise_IsDropped()
    {
        var map = Map(100, 80, (50, 50, 1, 1, 0.99f));

        Assert.Empty(TextBoxDetector.FindBoxes(map, 100, 80, Options));
    }

    [Fact]
    public void MinAreaRectangle_RotatedSquare_FitsItExactly()
    {
        var hull = TextBoxDetector.ConvexHull([new(0, 5), new(5, 0), new(10, 5), new(5, 10), new(5, 5)]);

        var rectangle = TextBoxDetector.MinAreaRectangle(hull);

        Assert.Equal(4, hull.Count);
        Assert.Equal(50, rectangle.Width * rectangle.Height, 3);
    }

    [Fact]
    public void OrderCorners_AnyOrder_StartsTopLeftClockwise()
    {
        Vector2[] corners = [new(10, 20), new(0, 0), new(10, 0), new(0, 20)];

        var ordered = TextBoxDetector.OrderCorners(corners);

        Assert.Equal([new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 20), new Vector2(0, 20)], ordered);
    }

    private static float[] Map(int width, int height, params (int X, int Y, int Width, int Height, float Value)[] blocks)
    {
        var map = new float[width * height];
        foreach (var block in blocks)
        {
            for (var y = block.Y; y < block.Y + block.Height; y++)
            {
                for (var x = block.X; x < block.X + block.Width; x++)
                {
                    map[(y * width) + x] = block.Value;
                }
            }
        }

        return map;
    }

    private static void AssertCorner(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 1);
        Assert.Equal(expected.Y, actual.Y, 1);
    }
}
