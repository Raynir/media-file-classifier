using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;

namespace MediaFileClassifier.Infrastructure.Hashing;

/// <summary>
/// Computes the hashes of a file in a granted folder.
/// </summary>
/// <param name="files">Reads the files.</param>
/// <param name="fileHasher">Computes fingerprints and content hashes.</param>
public sealed class MediaHasher(IMediaFileSource files, IFileHasher fileHasher) : IMediaHasher
{
    /// <inheritdoc />
    public async Task<UInt128> ComputeFingerprintAsync(FileLocation file, long sizeBytes, CancellationToken cancellationToken = default)
    {
        var stream = await files.OpenReadAsync(file, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await fileHasher.ComputeFingerprintAsync(stream, sizeBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<FileHashes> ComputeMissingAsync(
        FileLocation file,
        long sizeBytes,
        MediaType mediaType,
        FileHashes known,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(known);

        var hashes = known;
        if (hashes.Fingerprint is null)
        {
            hashes = hashes with { Fingerprint = await ComputeFingerprintAsync(file, sizeBytes, cancellationToken).ConfigureAwait(false) };
        }

        if (hashes.ContentHash is null)
        {
            var stream = await files.OpenReadAsync(file, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                hashes = hashes with { ContentHash = await fileHasher.ComputeContentHashAsync(stream, cancellationToken).ConfigureAwait(false) };
            }
        }

        return hashes;
    }
}
