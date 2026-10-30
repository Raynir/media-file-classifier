namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Content-based identity of a file, independent of its path (E01-S06). A value is <c>null</c> until it's computed.
/// </summary>
public sealed record FileHashes
{
    /// <summary>
    /// Gets the quick fingerprint: XxHash128 of the first, middle and last chunks, computed during discovery.
    /// It's only meaningful together with the file size.
    /// </summary>
    public UInt128? Fingerprint { get; init; }

    /// <summary>
    /// Gets the XxHash128 of the whole file, computed in the background.
    /// </summary>
    public UInt128? ContentHash { get; init; }

    /// <summary>
    /// Gets the 64-bit perceptual hash, for images and animated images only.
    /// </summary>
    public ulong? PerceptualHash { get; init; }
}
