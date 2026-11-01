using System.Buffers.Binary;
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
    private const byte GraphicControlLabel = 0xF9;
    private const byte ApplicationLabel = 0xFF;
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
        => (await ReadAsync(content, limit, cancellationToken)).FrameCount;

    /// <summary>
    /// Reads the frames, their delays (graphic control extensions) and the loop count (the NETSCAPE2.0 application
    /// extension), stopping at <paramref name="limit"/> frames. A truncated or damaged file returns what was found
    /// before the damage.
    /// </summary>
    /// <param name="content">The file, positioned at the start.</param>
    /// <param name="limit">Frame count at which to stop reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame structure.</returns>
    public static async ValueTask<AnimationInfo> ReadAsync(Stream content, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];

        // Header (6 bytes) and logical screen descriptor (7 bytes), whose packed field says if a global color table follows.
        if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 13), cancellationToken)
            || !await TrySkipColorTableAsync(content, buffer[10], cancellationToken))
        {
            return AnimationInfo.Empty;
        }

        var frames = 0;
        var duration = TimeSpan.Zero;
        var nextDelay = TimeSpan.Zero;
        int? loopCount = null;
        while (frames < limit && await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            switch (buffer[0])
            {
                case ImageSeparator:
                    frames++;
                    duration += nextDelay;
                    nextDelay = TimeSpan.Zero;
                    if (frames >= limit)
                    {
                        break;
                    }

                    // Image descriptor (9 bytes), local color table, LZW minimum code size (1 byte), image data.
                    if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 9), cancellationToken)
                        || !await TrySkipColorTableAsync(content, buffer[8], cancellationToken)
                        || !await TrySkipAsync(content, 1, cancellationToken)
                        || !await TrySkipSubBlocksAsync(content, buffer, cancellationToken))
                    {
                        return new(frames, duration, loopCount);
                    }

                    break;

                case ExtensionIntroducer:
                    if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
                    {
                        return new(frames, duration, loopCount);
                    }

                    var label = buffer[0];
                    var first = await TryReadSubBlockAsync(content, buffer, cancellationToken);
                    if (first is null)
                    {
                        return new(frames, duration, loopCount);
                    }

                    if (label == GraphicControlLabel && first == 4)
                    {
                        // Packed fields, delay in hundredths of a second, transparent color index.
                        nextDelay = TimeSpan.FromMilliseconds(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(1, 2)) * 10);
                    }
                    else if (label == ApplicationLabel && first == 11 && buffer.AsSpan(0, 11) is var application
                        && (application.SequenceEqual("NETSCAPE2.0"u8) || application.SequenceEqual("ANIMEXTS1.0"u8)))
                    {
                        // Sub-block 1 of the looping extension: [1, loop count low byte, high byte].
                        var second = await TryReadSubBlockAsync(content, buffer, cancellationToken);
                        if (second is null)
                        {
                            return new(frames, duration, loopCount);
                        }

                        if (second == 3 && buffer[0] == 1)
                        {
                            loopCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(1, 2));
                        }

                        if (second == 0)
                        {
                            break;
                        }
                    }

                    if (first != 0 && !await TrySkipSubBlocksAsync(content, buffer, cancellationToken))
                    {
                        return new(frames, duration, loopCount);
                    }

                    break;

                case Trailer:
                    return new(frames, duration, loopCount);

                default:
                    // Junk after a damaged block.
                    return new(frames, duration, loopCount);
            }
        }

        return new(frames, duration, loopCount);
    }

    private static ValueTask<bool> TrySkipColorTableAsync(Stream content, byte packedFields, CancellationToken cancellationToken)
        => (packedFields & ColorTableFlag) == 0
            ? ValueTask.FromResult(true)
            : TrySkipAsync(content, 3 << ((packedFields & 0x07) + 1), cancellationToken);

    /// <summary>
    /// Reads one data sub-block into the start of <paramref name="buffer"/>.
    /// </summary>
    /// <returns>Its length (<c>0</c> is the block terminator), or <c>null</c> at the end of the stream.</returns>
    private static async ValueTask<int?> TryReadSubBlockAsync(Stream content, byte[] buffer, CancellationToken cancellationToken)
    {
        if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            return null;
        }

        int length = buffer[0];
        return length == 0 || await TryReadExactlyAsync(content, buffer.AsMemory(0, length), cancellationToken) ? length : null;
    }

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
