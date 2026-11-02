using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// Computes the hashes of a file in a granted folder (E01-S06), reading it through <see cref="IMediaFileSource"/>.
/// </summary>
public interface IMediaHasher
{
    /// <summary>
    /// Computes the quick fingerprint, for discovery.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="sizeBytes">The file size in bytes, as listed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    Task<UInt128> ComputeFingerprintAsync(FileLocation file, long sizeBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the hashes that are still missing: the fingerprint, the full content hash and, for images and
    /// animated images, the perceptual hash. This reads the whole file, so it runs as a low-priority background
    /// job (E05). Hashes already known are kept.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="sizeBytes">The file size in bytes.</param>
    /// <param name="mediaType">The kind of media, which decides whether there's a perceptual hash.</param>
    /// <param name="known">Hashes already computed for the current content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All hashes. The perceptual hash stays <see langword="null"/> when the image can't be decoded.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    Task<FileHashes> ComputeMissingAsync(
        FileLocation file,
        long sizeBytes,
        MediaType mediaType,
        FileHashes known,
        CancellationToken cancellationToken = default);
}
