using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// Turns an <see cref="AiImage"/> into SigLIP 2 input (D092): upright, transparent areas over white, resized to
/// 256 x 256 without cropping by Pillow's bilinear filter, scaled to [-1, 1], channels first.
/// </summary>
internal static class Siglip2ImagePreprocessor
{
    /// <summary>
    /// Gets the number of floats one image takes in the input tensor.
    /// </summary>
    public const int ValuesPerImage = 3 * Siglip2Model.ImageSize * Siglip2Model.ImageSize;

    /// <summary>
    /// The shortest short side a scaled decode may have: twice the model input, so the resize still averages over
    /// real detail (chosen by the user, MFC-85).
    /// </summary>
    public const int MinDecodedShortSide = 512;

    /// <summary>
    /// Writes one image's input values.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="destination"><see cref="ValuesPerImage"/> floats: R plane, G plane, B plane.</param>
    /// <exception cref="InvalidDataException">The image can't be decoded.</exception>
    public static void Prepare(AiImage image, Span<float> destination)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (destination.Length < ValuesPerImage)
        {
            throw new ArgumentException($"The destination needs {ValuesPerImage} values.", nameof(destination));
        }

        var rgb = Resize(image);
        const int Plane = Siglip2Model.ImageSize * Siglip2Model.ImageSize;
        for (var i = 0; i < Plane; i++)
        {
            destination[i] = (rgb[i * 3] / 127.5f) - 1f;
            destination[Plane + i] = (rgb[(i * 3) + 1] / 127.5f) - 1f;
            destination[(2 * Plane) + i] = (rgb[(i * 3) + 2] / 127.5f) - 1f;
        }
    }

    /// <summary>
    /// Decodes the image if needed and resizes it to 256 x 256 RGB888.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The resized pixels.</returns>
    public static byte[] Resize(AiImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.IsRawRgba)
        {
            return PilBilinearResampler.ResizeToRgb(image.Data.Span, image.PixelWidth ?? 0, image.PixelHeight ?? 0, Siglip2Model.ImageSize, Siglip2Model.ImageSize);
        }

        var (pixels, width, height) = Decode(image);
        return PilBilinearResampler.ResizeToRgb(pixels, width, height, Siglip2Model.ImageSize, Siglip2Model.ImageSize);
    }

    /// <summary>
    /// Decodes an encoded image to upright RGBA, composited over white.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The pixels and their size.</returns>
    /// <param name="fullSize"><c>true</c> to decode every pixel, as the reference does; <c>false</c> to let JPEG decoding
    /// skip detail the resize would drop anyway.</param>
    internal static (byte[] Pixels, int Width, int Height) Decode(AiImage image, bool fullSize = false)
    {
        using var data = SKData.CreateCopy(image.Data.Span);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException($"The picture {image.Name} can't be decoded.");
        var size = fullSize ? codec.Info.Size : ScaledDecodeSize(codec);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = codec.GetPixels(info, out var pixels);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput))
        {
            throw new InvalidDataException($"The picture {image.Name} can't be decoded: {result}.");
        }

        CompositeOverWhite(pixels);
        return Orient(pixels, info.Width, info.Height, codec.EncodedOrigin);
    }

    /// <summary>
    /// Picks the smallest size the decoder can produce directly (JPEG: 1/2, 1/4 or 1/8 by DCT scaling) whose short
    /// side is still at least <see cref="MinDecodedShortSide"/>. Other formats decode at full size.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <returns>The size to decode at.</returns>
    internal static SKSizeI ScaledDecodeSize(SKCodec codec)
    {
        var size = codec.Info.Size;
        foreach (var scale in (ReadOnlySpan<float>)[0.125f, 0.25f, 0.5f])
        {
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled != size && Math.Min(scaled.Width, scaled.Height) >= MinDecodedShortSide)
            {
                return scaled;
            }
        }

        return size;
    }

    /// <summary>
    /// Blends transparent pixels over opaque white with Pillow's <c>alpha_composite</c> integer rounding.
    /// </summary>
    /// <param name="rgba">Unpremultiplied RGBA pixels, changed in place; alpha becomes 255.</param>
    internal static void CompositeOverWhite(Span<byte> rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            uint alpha = rgba[i + 3];
            if (alpha == 255)
            {
                continue;
            }

            if (alpha == 0)
            {
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
            }
            else
            {
                var sourceWeight = alpha << 7;
                var whiteWeight = (255u << 7) - sourceWeight;
                for (var c = 0; c < 3; c++)
                {
                    var value = (rgba[i + c] * sourceWeight) + (255 * whiteWeight) + (0x80u << 7);
                    rgba[i + c] = (byte)((((value >> 8) + value) >> 8) >> 7);
                }
            }

            rgba[i + 3] = 255;
        }
    }

    internal static (byte[] Pixels, int Width, int Height) Orient(byte[] pixels, int width, int height, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft)
        {
            return (pixels, width, height);
        }

        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (outWidth, outHeight) = swaps ? (height, width) : (width, height);
        var result = new byte[pixels.Length];
        var source = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        var target = MemoryMarshal.Cast<byte, uint>(result.AsSpan());
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                var (sx, sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (width - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, height - 1 - x),
                    SKEncodedOrigin.RightBottom => (width - 1 - y, height - 1 - x),
                    SKEncodedOrigin.LeftBottom => (width - 1 - y, x),
                    _ => (x, y),
                };
                target[(y * outWidth) + x] = source[(sy * width) + sx];
            }
        }

        return (result, outWidth, outHeight);
    }
}
