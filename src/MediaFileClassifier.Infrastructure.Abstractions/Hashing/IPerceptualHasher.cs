namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// Computes the 64-bit perceptual hash of an image (E01-S06-T03). Visually similar images, e.g. a resized or
/// re-compressed copy, get hashes a small Hamming distance apart (<see cref="PerceptualHash.Distance"/>).
/// </summary>
public interface IPerceptualHasher
{
    /// <summary>
    /// Computes the perceptual hash of an image as it's displayed, after its EXIF orientation. Animated images are
    /// hashed by their first frame. It's CPU-bound, so call it from a background worker.
    /// </summary>
    /// <param name="content">The image file's content.</param>
    /// <returns>The hash, or <see langword="null"/> when the content isn't an image this platform can decode.</returns>
    ulong? Compute(Stream content);

    /// <summary>
    /// Computes the perceptual hash of decoded pixels, such as a sampled video frame (E06-S02).
    /// </summary>
    /// <param name="rgba">Unpremultiplied RGBA pixels, 8 bits per channel, row-major with no padding, upright.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="ArgumentException">The buffer doesn't hold <c>width * height</c> pixels.</exception>
    ulong ComputeFromPixels(ReadOnlySpan<byte> rgba, int width, int height);
}
