using System.Numerics;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// An RGBA picture in memory.
/// </summary>
/// <param name="Pixels">The pixels, 4 bytes each, row by row.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
internal sealed record RgbaPicture(byte[] Pixels, int Width, int Height);

/// <summary>
/// The picture operations of the PP-OCR pipeline: resizing, cutting out a rotated text box, and filling the
/// normalised BGR tensors the models take (PaddleOCR reads pictures with OpenCV, so its models expect BGR).
/// </summary>
internal static class OcrImageOps
{
    /// <summary>
    /// Resizes a picture with bilinear sampling (pixel centres aligned, as OpenCV's <c>INTER_LINEAR</c>).
    /// </summary>
    /// <param name="source">The picture.</param>
    /// <param name="width">New width.</param>
    /// <param name="height">New height.</param>
    /// <returns>The resized picture.</returns>
    public static RgbaPicture Resize(RgbaPicture source, int width, int height)
    {
        if (width == source.Width && height == source.Height)
        {
            return source;
        }

        var scaleX = (float)source.Width / width;
        var scaleY = (float)source.Height / height;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var sy = ((y + 0.5f) * scaleY) - 0.5f;
            for (var x = 0; x < width; x++)
            {
                Sample(source, ((x + 0.5f) * scaleX) - 0.5f, sy, pixels.AsSpan(((y * width) + x) * 4, 4));
            }
        }

        return new RgbaPicture(pixels, width, height);
    }

    /// <summary>
    /// Cuts out a text box as an upright picture, the way PaddleOCR's <c>get_rotate_crop_image</c> does: the box is
    /// mapped onto a rectangle as wide and high as its longer sides, and a box much taller than wide is turned a quarter
    /// to the left (vertical text).
    /// </summary>
    /// <param name="source">The picture.</param>
    /// <param name="corners">The box: top left, top right, bottom right, bottom left, in picture pixels.</param>
    /// <returns>The cut-out, or <c>null</c> if the box is under a pixel wide or high.</returns>
    public static RgbaPicture? CropBox(RgbaPicture source, Vector2[] corners)
    {
        var width = (int)Math.Max(Vector2.Distance(corners[0], corners[1]), Vector2.Distance(corners[3], corners[2]));
        var height = (int)Math.Max(Vector2.Distance(corners[0], corners[3]), Vector2.Distance(corners[1], corners[2]));
        if (width < 1 || height < 1)
        {
            return null;
        }

        var across = (corners[1] - corners[0]) / width;
        var down = (corners[3] - corners[0]) / height;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = corners[0] + (across * x) + (down * y);
                Sample(source, point.X, point.Y, pixels.AsSpan(((y * width) + x) * 4, 4));
            }
        }

        var crop = new RgbaPicture(pixels, width, height);
        return height >= width * 1.5 ? RotateLeft(crop) : crop;
    }

    /// <summary>
    /// Writes a picture into a CHW tensor as BGR, each value scaled to -1 to 1 (mean and deviation 0.5).
    /// </summary>
    /// <param name="picture">The picture.</param>
    /// <param name="tensor">The tensor's data.</param>
    /// <param name="offset">Where this picture's planes start.</param>
    /// <param name="planeWidth">Width of a plane, at least the picture's; the rest of each row is left as it is.</param>
    /// <param name="planeHeight">Height of a plane, at least the picture's.</param>
    public static void WriteBgr(RgbaPicture picture, Span<float> tensor, int offset, int planeWidth, int planeHeight)
    {
        var plane = planeWidth * planeHeight;
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var source = ((y * picture.Width) + x) * 4;
                var target = offset + (y * planeWidth) + x;
                tensor[target] = Normalize(picture.Pixels[source + 2]);
                tensor[target + plane] = Normalize(picture.Pixels[source + 1]);
                tensor[target + (2 * plane)] = Normalize(picture.Pixels[source]);
            }
        }
    }

    private static float Normalize(byte value) => ((value / 255f) - 0.5f) / 0.5f;

    private static RgbaPicture RotateLeft(RgbaPicture source)
    {
        // np.rot90: the top row becomes the left column, read bottom to top.
        var pixels = new byte[source.Pixels.Length];
        int width = source.Height, height = source.Width;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var from = ((x * source.Width) + (source.Width - 1 - y)) * 4;
                source.Pixels.AsSpan(from, 4).CopyTo(pixels.AsSpan(((y * width) + x) * 4, 4));
            }
        }

        return new RgbaPicture(pixels, width, height);
    }

    // Bilinear sample with the edge pixels repeated outside the picture.
    private static void Sample(RgbaPicture source, float x, float y, Span<byte> destination)
    {
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, source.Width - 1);
        var y1 = Math.Min(y0 + 1, source.Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var pixels = source.Pixels;
        for (var channel = 0; channel < 4; channel++)
        {
            var top = (pixels[(((y0 * source.Width) + x0) * 4) + channel] * (1 - fx)) + (pixels[(((y0 * source.Width) + x1) * 4) + channel] * fx);
            var bottom = (pixels[(((y1 * source.Width) + x0) * 4) + channel] * (1 - fx)) + (pixels[(((y1 * source.Width) + x1) * 4) + channel] * fx);
            destination[channel] = (byte)Math.Clamp((int)MathF.Round((top * (1 - fy)) + (bottom * fy)), 0, 255);
        }
    }
}
