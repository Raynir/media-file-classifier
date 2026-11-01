using static MediaFileClassifier.Infrastructure.Media.Formats.StreamReading;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// Walks the blocks of a GIF file without decoding any pixels (GIF89a specification).
/// </summary>
internal static class GifStructure
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte Trailer = 0x3B;
    private const byte ColorTableFlag = 0x80;

    /// <summary>
    /// Counts the frames (image descriptors), stopping at <paramref name="limit"/>. A truncated or damaged file returns
    /// the frames found before the damage.
    /// </summary>
    /// <param name="content">The file, positioned at the start.</param>
    /// <param name="limit">Frame count at which to stop reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of frames, at most <paramref name="limit"/>.</returns>
    public static async ValueTask<int> CountFramesAsync(Stream content, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[13];

        // Header (6 bytes) and logical screen descriptor (7 bytes), whose packed field says if a global color table follows.
        if (!await TryReadExactlyAsync(content, buffer, cancellationToken)
            || !await TrySkipColorTableAsync(content, buffer[10], cancellationToken))
        {
            return 0;
        }

        var frames = 0;
        while (await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            switch (buffer[0])
            {
                case ImageSeparator:
                    if (++frames >= limit)
                    {
                        return frames;
                    }

                    // Image descriptor (9 bytes), local color table, LZW minimum code size (1 byte), image data.
                    if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 9), cancellationToken)
                        || !await TrySkipColorTableAsync(content, buffer[8], cancellationToken)
                        || !await TrySkipAsync(content, 1, cancellationToken)
                        || !await TrySkipSubBlocksAsync(content, buffer, cancellationToken))
                    {
                        return frames;
                    }

                    break;

                case ExtensionIntroducer:
                    // Label (1 byte), then data sub-blocks.
                    if (!await TrySkipAsync(content, 1, cancellationToken)
                        || !await TrySkipSubBlocksAsync(content, buffer, cancellationToken))
                    {
                        return frames;
                    }

                    break;

                case Trailer:
                    return frames;

                default:
                    // Junk after a damaged block.
                    return frames;
            }
        }

        return frames;
    }

    private static ValueTask<bool> TrySkipColorTableAsync(Stream content, byte packedFields, CancellationToken cancellationToken)
        => (packedFields & ColorTableFlag) == 0
            ? ValueTask.FromResult(true)
            : TrySkipAsync(content, 3 << ((packedFields & 0x07) + 1), cancellationToken);

    private static async ValueTask<bool> TrySkipSubBlocksAsync(Stream content, byte[] buffer, CancellationToken cancellationToken)
    {
        while (await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            if (buffer[0] == 0)
            {
                return true;
            }

            if (!await TrySkipAsync(content, buffer[0], cancellationToken))
            {
                return false;
            }
        }

        return false;
    }
}
