using System.Numerics;
using MediaFileClassifier.Infrastructure.AI.Ocr;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Ocr;

/// <summary>
/// Tests for <see cref="OcrLineLayout"/> and the detector's input size.
/// </summary>
public sealed class OcrLineLayoutTests
{
    [Fact]
    public void Join_WordsOfTwoLines_ReadsLeftToRightThenTopToBottom()
    {
        var boxes = new List<(Vector2[], string)>
        {
            (Box(120, 52, 60, 20), "ROAD"),
            (Box(10, 10, 80, 20), "STOP"),
            (Box(100, 13, 50, 20), "HERE"),
            (Box(10, 50, 100, 22), "MAIN"),
        };

        Assert.Equal("STOP HERE\nMAIN ROAD", OcrLineLayout.Join(boxes));
    }

    [Fact]
    public void Join_NoBoxes_IsEmpty()
    {
        Assert.Equal(string.Empty, OcrLineLayout.Join([]));
    }

    [Theory]
    [InlineData(1920, 1270, 1920, 1280)] // Big enough: only rounded.
    [InlineData(400, 300, 992, 736)] // Short side enlarged to 736.
    [InlineData(4000, 200, 1984, 96)] // Enlarging would pass the long-side cap, so the cap wins.
    [InlineData(10, 10, 736, 736)]
    public void DetectionSize_Picture_ScalesToTheLimitsInSteps32(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), RapidOcrOnnxModel.DetectionSize(width, height, 736, 2000));
    }

    private static Vector2[] Box(float x, float y, float width, float height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];
}
