using System.Buffers;
using System.IO.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;

namespace MediaFileClassifier.Infrastructure.Hashing;

/// <summary>
/// Computes fingerprints and content hashes with XxHash128 (<c>System.IO.Hashing</c>).
/// </summary>
public sealed class XxHashFileHasher : IFileHasher
{
    /// <summary>
    /// Size of each fingerprint chunk. Changing it changes every fingerprint, so stored fingerprints would no longer match.
    /// </summary>
    public const int DefaultChunkSize = 64 * 1024;

    private const int ContentBufferSize = 1024 * 1024;

    private readonly int chunkSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="XxHashFileHasher"/> class with the default chunk size.
    /// </summary>
    public XxHashFileHasher()
        : this(DefaultChunkSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XxHashFileHasher"/> class.
    /// </summary>
    /// <param name="chunkSize">Size of each fingerprint chunk, for tuning.</param>
    public XxHashFileHasher(int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        this.chunkSize = chunkSize;
    }

    /// <inheritdoc />
    public async Task<UInt128> ComputeFingerprintAsync(Stream content, long length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length <= 3L * chunkSize)
        {
            return await ComputeContentHashAsync(content, cancellationToken).ConfigureAwait(false);
        }

        var hash = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            long position = 0;
            foreach (var offset in (long[])[0, (length - chunkSize) / 2, length - chunkSize])
            {
                position = await SkipToAsync(content, position, offset, buffer, cancellationToken).ConfigureAwait(false);
                await content.ReadExactlyAsync(buffer.AsMemory(0, chunkSize), cancellationToken).ConfigureAwait(false);
                position += chunkSize;
                hash.Append(buffer.AsSpan(0, chunkSize));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetCurrentHashAsUInt128();
    }

    /// <inheritdoc />
    public async Task<UInt128> ComputeContentHashAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hash = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(ContentBufferSize);
        try
        {
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, ContentBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetCurrentHashAsUInt128();
    }

    private static async Task<long> SkipToAsync(Stream content, long position, long offset, byte[] buffer, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Seek(offset - position, SeekOrigin.Current);
            return offset;
        }

        // Android content streams may not seek, so read through to the chunk.
        while (position < offset)
        {
            var count = (int)Math.Min(buffer.Length, offset - position);
            await content.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            position += count;
        }

        return position;
    }
}
