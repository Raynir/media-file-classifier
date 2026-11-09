namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Decodes a still image once for every analyzer of an item (E06-S01-T02): upright, transparent areas over white.
/// </summary>
public interface IAnalysisImageDecoder
{
    /// <summary>
    /// Decodes an encoded picture at the smallest size that still meets both limits. JPEG decoding may scale by 1/2,
    /// 1/4 or 1/8; other formats decode at full size. Pictures smaller than the limits decode at full size.
    /// </summary>
    /// <param name="encoded">The file's bytes.</param>
    /// <param name="minShortSide">The smallest short side wanted, after orientation.</param>
    /// <param name="minLongestEdge">The smallest longest edge wanted, after orientation.</param>
    /// <param name="name">Name for error messages.</param>
    /// <returns>The pixels.</returns>
    /// <exception cref="InvalidDataException">The picture can't be decoded.</exception>
    DecodedImage Decode(ReadOnlyMemory<byte> encoded, int minShortSide, int minLongestEdge, string? name = null);

    /// <summary>
    /// Shrinks a decoded picture so its longest edge is at most <paramref name="maxLongestEdge"/>. Smaller pictures
    /// are returned as they are.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="maxLongestEdge">The largest longest edge.</param>
    /// <returns>The picture, shrunk if needed.</returns>
    DecodedImage Shrink(DecodedImage image, int maxLongestEdge);
}
