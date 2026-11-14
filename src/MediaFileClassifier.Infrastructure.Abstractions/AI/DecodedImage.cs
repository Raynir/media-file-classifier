namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An upright decoded picture: RGBA8888, row-major, stride <c>Width * 4</c>, alpha 255.
/// </summary>
/// <param name="Pixels">The pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record DecodedImage(ReadOnlyMemory<byte> Pixels, int Width, int Height)
{
    /// <summary>
    /// Gets the longest edge in pixels.
    /// </summary>
    public int LongestEdge => Math.Max(Width, Height);

    /// <summary>
    /// Wraps the pixels as a raw <see cref="AiImage"/> for a provider.
    /// </summary>
    /// <param name="name">Name for logs and errors.</param>
    /// <returns>The image.</returns>
    public AiImage ToAiImage(string? name = null) => AiImage.FromRgba(Pixels, Width, Height, name);
}
