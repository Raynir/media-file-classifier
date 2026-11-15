using System.Numerics;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// Puts recognised boxes into reading order: boxes that share most of their height are one line, read left to right,
/// and lines go top to bottom.
/// </summary>
internal static class OcrLineLayout
{
    /// <summary>
    /// How much of the shorter box's height two boxes must share to be on one line.
    /// </summary>
    internal const float SameLineOverlap = 0.5f;

    /// <summary>
    /// Joins boxes into text: a space between the boxes of a line, a line break between lines.
    /// </summary>
    /// <param name="boxes">The boxes, with their corners and text.</param>
    /// <returns>The text.</returns>
    public static string Join(IReadOnlyList<(Vector2[] Corners, string Text)> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        var lines = new List<List<(float Top, float Bottom, float Left, string Text)>>();
        foreach (var box in boxes
            .Select(box => (Top: box.Corners.Min(point => point.Y), Bottom: box.Corners.Max(point => point.Y), Left: box.Corners.Min(point => point.X), box.Text))
            .OrderBy(box => (box.Top + box.Bottom) / 2))
        {
            var line = lines.FirstOrDefault(line => SharesLine(line, box.Top, box.Bottom));
            if (line is null)
            {
                lines.Add([box]);
            }
            else
            {
                line.Add(box);
            }
        }

        return string.Join(
            '\n',
            lines
                .OrderBy(line => line.Average(box => box.Top))
                .Select(line => string.Join(' ', line.OrderBy(box => box.Left).Select(box => box.Text))));
    }

    private static bool SharesLine(List<(float Top, float Bottom, float Left, string Text)> line, float top, float bottom)
    {
        var lineTop = line.Average(box => box.Top);
        var lineBottom = line.Average(box => box.Bottom);
        var overlap = Math.Min(lineBottom, bottom) - Math.Max(lineTop, top);
        return overlap >= SameLineOverlap * Math.Min(lineBottom - lineTop, bottom - top);
    }
}
