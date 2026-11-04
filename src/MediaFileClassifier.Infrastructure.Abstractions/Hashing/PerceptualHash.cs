using System.Numerics;

namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// Compares 64-bit perceptual hashes.
/// </summary>
public static class PerceptualHash
{
    /// <summary>
    /// The largest distance at which two images count as the same picture, e.g. a resized, re-compressed or
    /// converted copy. On the robustness test set, such copies are 0 to 2 bits apart and unrelated images at least
    /// 10 (E01-S06-T06).
    /// </summary>
    public const int SamePictureThreshold = 8;

    /// <summary>
    /// Gets the Hamming distance between two hashes: the number of bits that differ, from 0 (same) to 64.
    /// </summary>
    /// <param name="first">First hash.</param>
    /// <param name="second">Second hash.</param>
    /// <returns>The distance.</returns>
    public static int Distance(ulong first, ulong second) => BitOperations.PopCount(first ^ second);
}
