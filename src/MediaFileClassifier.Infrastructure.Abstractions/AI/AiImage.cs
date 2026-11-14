namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An image to analyze: an encoded file (JPEG, PNG, WebP…), or decoded pixels such as a frame sampled from a video
/// (<see cref="FromRgba"/>). Providers decode or resize it as their model needs.
/// </summary>
/// <param name="Data">Encoded image bytes, or RGBA8888 pixels when <see cref="MediaType"/> is <see cref="RawRgbaMediaType"/>.</param>
/// <param name="MediaType">MIME type, e.g. <c>image/jpeg</c>.</param>
/// <param name="Name">Name for logs and error messages, such as the item's file name or a frame's time.</param>
public sealed record AiImage(ReadOnlyMemory<byte> Data, string MediaType, string? Name = null)
{
    /// <summary>
    /// The media type of decoded pixels: RGBA8888, row-major, stride <c>width * 4</c>, upright.
    /// </summary>
    public const string RawRgbaMediaType = "image/x-raw-rgba";

    /// <summary>
    /// Gets the pixel width of decoded pixels, or <c>null</c> for an encoded image.
    /// </summary>
    public int? PixelWidth { get; init; }

    /// <summary>
    /// Gets the pixel height of decoded pixels, or <c>null</c> for an encoded image.
    /// </summary>
    public int? PixelHeight { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Data"/> holds decoded RGBA pixels.
    /// </summary>
    public bool IsRawRgba => string.Equals(MediaType, RawRgbaMediaType, StringComparison.Ordinal);

    /// <summary>
    /// Wraps decoded pixels, e.g. a <c>SampledFrame</c> (E06-S02), so a provider doesn't decode them again.
    /// </summary>
    /// <param name="pixels">RGBA8888 pixels, row-major, stride <c>width * 4</c>, upright. Alpha is ignored.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="name">Name for logs and error messages.</param>
    /// <returns>The image.</returns>
    public static AiImage FromRgba(ReadOnlyMemory<byte> pixels, int width, int height, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != (long)width * height * 4)
        {
            throw new ArgumentException($"{width} x {height} RGBA pixels need {(long)width * height * 4} bytes, not {pixels.Length}.", nameof(pixels));
        }

        return new AiImage(pixels, RawRgbaMediaType, name) { PixelWidth = width, PixelHeight = height };
    }
}
