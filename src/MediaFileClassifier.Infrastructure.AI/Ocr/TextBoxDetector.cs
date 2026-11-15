using System.Numerics;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// A text box found by the detector: a rotated rectangle, with its corners clockwise from the top left.
/// </summary>
/// <param name="Corners">Top left, top right, bottom right and bottom left.</param>
/// <param name="Score">The detector's mean probability inside the box.</param>
internal sealed record TextBox(Vector2[] Corners, float Score);

/// <summary>
/// Turns the probability map of a DB text detector (PP-OCR) into text boxes, the way PaddleOCR's <c>DBPostProcess</c>
/// does with OpenCV: threshold, dilate, find the connected regions, fit a minimum-area rectangle to each, score it,
/// and grow it by the unclip ratio. It needs no OpenCV, so it runs on every head.
/// </summary>
internal static class TextBoxDetector
{
    /// <summary>
    /// Finds the text boxes in a probability map.
    /// </summary>
    /// <param name="probabilities">The map, row by row.</param>
    /// <param name="width">Map width.</param>
    /// <param name="height">Map height.</param>
    /// <param name="options">Thresholds.</param>
    /// <returns>The boxes, in map coordinates.</returns>
    public static List<TextBox> FindBoxes(ReadOnlySpan<float> probabilities, int width, int height, TextDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (probabilities.Length != width * height)
        {
            throw new ArgumentException("The map doesn't match its size.", nameof(probabilities));
        }

        var mask = new bool[width * height];
        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] = probabilities[i] > options.BinaryThreshold;
        }

        mask = Dilate(mask, width, height);
        var boxes = new List<TextBox>();
        foreach (var points in FindRegions(mask, width, height, options.MaxCandidates))
        {
            var rectangle = MinAreaRectangle(ConvexHull(points));
            if (Math.Min(rectangle.Width, rectangle.Height) < options.MinBoxSide)
            {
                continue;
            }

            var score = MeanInside(probabilities, width, height, rectangle.Corners());
            if (score < options.BoxThreshold)
            {
                continue;
            }

            // PaddleOCR offsets the polygon by area * ratio / perimeter (pyclipper) and fits a rectangle again; for a
            // rectangle that's the same rectangle, grown by that distance on every side.
            var distance = rectangle.Width * rectangle.Height * options.UnclipRatio / (2 * (rectangle.Width + rectangle.Height));
            var grown = rectangle with { Width = rectangle.Width + (2 * distance), Height = rectangle.Height + (2 * distance) };
            if (Math.Min(grown.Width, grown.Height) < options.MinBoxSide + 2)
            {
                continue;
            }

            boxes.Add(new TextBox(OrderCorners(grown.Corners()), score));
        }

        return boxes;
    }

    /// <summary>
    /// Orders four corners clockwise from the top left, the way PaddleOCR's <c>get_mini_boxes</c> does: the two leftmost
    /// points are the left side, and the upper one of each side is its top.
    /// </summary>
    /// <param name="corners">The corners, in any order.</param>
    /// <returns>Top left, top right, bottom right, bottom left.</returns>
    internal static Vector2[] OrderCorners(Vector2[] corners)
    {
        var sorted = corners.OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        var (topLeft, bottomLeft) = sorted[0].Y <= sorted[1].Y ? (sorted[0], sorted[1]) : (sorted[1], sorted[0]);
        var (topRight, bottomRight) = sorted[2].Y <= sorted[3].Y ? (sorted[2], sorted[3]) : (sorted[3], sorted[2]);
        return [topLeft, topRight, bottomRight, bottomLeft];
    }

    /// <summary>
    /// Finds the convex hull of points (Andrew's monotone chain).
    /// </summary>
    /// <param name="points">The points.</param>
    /// <returns>The hull, counter-clockwise in image coordinates, without repeated points.</returns>
    internal static List<Vector2> ConvexHull(List<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        var lower = HalfHull(sorted);
        sorted.Reverse();
        var upper = HalfHull(sorted);
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static List<Vector2> HalfHull(List<Vector2> sorted)
    {
        var half = new List<Vector2>();
        foreach (var point in sorted)
        {
            while (half.Count >= 2 && Cross(half[^2], half[^1], point) <= 0)
            {
                half.RemoveAt(half.Count - 1);
            }

            half.Add(point);
        }

        return half;
    }

    /// <summary>
    /// Fits the rectangle of least area around a convex hull (rotating calipers: one side lies on a hull edge).
    /// </summary>
    /// <param name="hull">The hull.</param>
    /// <returns>The rectangle.</returns>
    internal static RotatedRectangle MinAreaRectangle(List<Vector2> hull)
    {
        if (hull.Count == 0)
        {
            return new RotatedRectangle(Vector2.Zero, Vector2.UnitX, 0, 0);
        }

        if (hull.Count == 1)
        {
            return new RotatedRectangle(hull[0], Vector2.UnitX, 0, 0);
        }

        var best = default(RotatedRectangle);
        var bestArea = float.MaxValue;
        for (var i = 0; i < hull.Count; i++)
        {
            var edge = hull[(i + 1) % hull.Count] - hull[i];
            if (edge.LengthSquared() == 0)
            {
                continue;
            }

            var axis = Vector2.Normalize(edge);
            var normal = new Vector2(-axis.Y, axis.X);
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var point in hull)
            {
                var u = Vector2.Dot(point, axis);
                var v = Vector2.Dot(point, normal);
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            var area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area;
                var center = (axis * ((minU + maxU) / 2)) + (normal * ((minV + maxV) / 2));
                best = new RotatedRectangle(center, axis, maxU - minU, maxV - minV);
            }
        }

        return best;
    }

    private static float Cross(Vector2 origin, Vector2 a, Vector2 b) =>
        ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

    // A 2 x 2 dilation with OpenCV's default anchor: a pixel is set when it or its left, upper or upper-left neighbour is.
    private static bool[] Dilate(bool[] mask, int width, int height)
    {
        var result = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[(y * width) + x] = mask[(y * width) + x]
                    || (x > 0 && mask[(y * width) + x - 1])
                    || (y > 0 && mask[((y - 1) * width) + x])
                    || (x > 0 && y > 0 && mask[((y - 1) * width) + x - 1]);
            }
        }

        return result;
    }

    // Labels the 8-connected regions and returns, for each, the leftmost and rightmost pixel of every row: enough for
    // its convex hull.
    private static IEnumerable<List<Vector2>> FindRegions(bool[] mask, int width, int height, int maxRegions)
    {
        var labels = new int[mask.Length];
        var stack = new Stack<int>();
        var regions = 0;
        for (var start = 0; start < mask.Length && regions < maxRegions; start++)
        {
            if (!mask[start] || labels[start] != 0)
            {
                continue;
            }

            regions++;
            var rows = new Dictionary<int, (int Min, int Max)>();
            labels[start] = regions;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                int x = index % width, y = index / width;
                rows[y] = rows.TryGetValue(y, out var row) ? (Math.Min(row.Min, x), Math.Max(row.Max, x)) : (x, x);
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var next = (ny * width) + nx;
                        if (nx >= 0 && nx < width && mask[next] && labels[next] == 0)
                        {
                            labels[next] = regions;
                            stack.Push(next);
                        }
                    }
                }
            }

            var points = new List<Vector2>(rows.Count * 2);
            foreach (var (y, (min, max)) in rows)
            {
                points.Add(new Vector2(min, y));
                points.Add(new Vector2(max, y));
            }

            yield return points;
        }
    }

    // The mean probability inside a box (PaddleOCR's "fast" score), over the pixels of its bounding rectangle that
    // fall inside it.
    private static float MeanInside(ReadOnlySpan<float> probabilities, int width, int height, Vector2[] corners)
    {
        var minX = Math.Clamp((int)Math.Floor(corners.Min(point => point.X)), 0, width - 1);
        var maxX = Math.Clamp((int)Math.Ceiling(corners.Max(point => point.X)), 0, width - 1);
        var minY = Math.Clamp((int)Math.Floor(corners.Min(point => point.Y)), 0, height - 1);
        var maxY = Math.Clamp((int)Math.Ceiling(corners.Max(point => point.Y)), 0, height - 1);
        double sum = 0;
        var count = 0;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (IsInside(corners, new Vector2(x, y)))
                {
                    sum += probabilities[(y * width) + x];
                    count++;
                }
            }
        }

        return count == 0 ? 0 : (float)(sum / count);
    }

    // Whether a point is inside (or on) a convex polygon, whatever its winding.
    private static bool IsInside(Vector2[] polygon, Vector2 point)
    {
        bool positive = false, negative = false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var cross = Cross(polygon[i], polygon[(i + 1) % polygon.Length], point);
            positive |= cross > 1e-3f;
            negative |= cross < -1e-3f;
        }

        return !(positive && negative);
    }
}

/// <summary>
/// A rectangle at any angle.
/// </summary>
/// <param name="Center">Its center.</param>
/// <param name="Axis">The unit direction of its width.</param>
/// <param name="Width">Its extent along <paramref name="Axis"/>.</param>
/// <param name="Height">Its extent across <paramref name="Axis"/>.</param>
internal readonly record struct RotatedRectangle(Vector2 Center, Vector2 Axis, float Width, float Height)
{
    /// <summary>
    /// Gets its four corners, in order around it.
    /// </summary>
    /// <returns>The corners.</returns>
    public Vector2[] Corners()
    {
        var u = Axis * (Width / 2);
        var v = new Vector2(-Axis.Y, Axis.X) * (Height / 2);
        return [Center - u - v, Center + u - v, Center + u + v, Center - u + v];
    }
}

/// <summary>
/// Thresholds of the DB text detector's post-processing, with RapidOCR's defaults.
/// </summary>
internal sealed record TextDetectionOptions
{
    /// <summary>Gets the probability above which a pixel is text.</summary>
    public float BinaryThreshold { get; init; } = 0.3f;

    /// <summary>Gets the mean probability a box needs to be kept.</summary>
    public float BoxThreshold { get; init; } = 0.5f;

    /// <summary>Gets how much a box grows, relative to its area over its perimeter.</summary>
    public float UnclipRatio { get; init; } = 1.6f;

    /// <summary>Gets the shortest side, in map pixels, a box needs before it grows.</summary>
    public float MinBoxSide { get; init; } = 3;

    /// <summary>Gets the most regions looked at.</summary>
    public int MaxCandidates { get; init; } = 1000;
}
