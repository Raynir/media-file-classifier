namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// Computes the content-based identity of a file (E01-S06): the quick fingerprint and the full content hash.
/// Both are XxHash128, so they're stable across platforms and app versions.
/// </summary>
public interface IFileHasher
{
    /// <summary>
    /// Computes the quick fingerprint: XxHash128 of the first, middle and last chunks of the file, or of the whole
    /// file when it's no larger than three chunks. It's cheap enough for discovery, even for large videos, but only
    /// meaningful together with the file size.
    /// </summary>
    /// <param name="content">The file's content, positioned at the start. Seekable streams are read in three chunks.</param>
    /// <param name="length">The file size in bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fingerprint.</returns>
    Task<UInt128> ComputeFingerprintAsync(Stream content, long length, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the XxHash128 of the whole file, streaming. Run it in the background (E05): it reads every byte.
    /// </summary>
    /// <param name="content">The file's content, positioned at the start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash.</returns>
    Task<UInt128> ComputeContentHashAsync(Stream content, CancellationToken cancellationToken = default);
}
