namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// The tier of file matching that found a match (E01-S06-T05), in the order they're tried.
/// </summary>
public enum FileMatchType
{
    /// <summary>
    /// No tier matched.
    /// </summary>
    None,

    /// <summary>
    /// The full content hash is the same: a byte-identical file.
    /// </summary>
    ContentHash,

    /// <summary>
    /// The quick fingerprint and the size are the same, and the content hashes don't differ.
    /// </summary>
    Fingerprint,

    /// <summary>
    /// The perceptual hashes are at most <see cref="Hashing.PerceptualHash.SamePictureThreshold"/> bits apart:
    /// the same picture, e.g. resized, re-compressed or converted. Images and animated images only.
    /// </summary>
    Perceptual,

    /// <summary>
    /// The same file name (ignoring case) and media type, and the same size or modification time (to the second).
    /// </summary>
    FileNameAndMetadata,
}
