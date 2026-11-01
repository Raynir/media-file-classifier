using System.Buffers.Binary;
using static MediaFileClassifier.Infrastructure.Media.Formats.StreamReading;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// Walks the RIFF chunks of a WebP file without decoding any pixels (RFC 9649).
/// </summary>
internal static class WebPStructure
{
    private const int RiffHeaderLength = 12;
    private const byte AnimationFlag = 0x02;

    /// <summary>
    /// Counts the frames, stopping at <paramref name="limit"/>. A simple file (<c>VP8 </c> or <c>VP8L</c>) and an
    /// extended file without the animation flag have one frame; an animated file has one <c>ANMF</c> chunk per frame.
    /// A truncated file returns the frames found before the end.
    /// </summary>
    /// <param name="content">The file, positioned at the start.</param>
    /// <param name="limit">Frame count at which to stop reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of frames, at most <paramref name="limit"/>.</returns>
    public static async ValueTask<int> CountFramesAsync(Stream content, int limit, CancellationToken cancellationToken)
        => (await ReadAsync(content, limit, cancellationToken)).FrameCount;

    /// <summary>
    /// Reads the frames, their durations (<c>ANMF</c> chunks) and the loop count (the <c>ANIM</c> chunk), stopping at
    /// <paramref name="limit"/> frames. A still image has one frame and no duration. A truncated file returns what was
    /// found before the end.
    /// </summary>
    /// <param name="content">The file, positioned at the start.</param>
    /// <param name="limit">Frame count at which to stop reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame structure.</returns>
    public static async ValueTask<AnimationInfo> ReadAsync(Stream content, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[16];
        if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, RiffHeaderLength), cancellationToken)
            || !await TryReadExactlyAsync(content, buffer.AsMemory(0, 8), cancellationToken))
        {
            return AnimationInfo.Empty;
        }

        var still = new AnimationInfo(1, TimeSpan.Zero, null);

        // The first chunk tells the layout: only an extended (VP8X) file can be animated, and its flags say whether it is.
        if (!buffer.AsSpan(0, 4).SequenceEqual("VP8X"u8))
        {
            return still;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));
        if (size == 0 || !await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            return AnimationInfo.Empty;
        }

        if ((buffer[0] & AnimationFlag) == 0)
        {
            return still;
        }

        var frames = 0;
        var duration = TimeSpan.Zero;
        int? loopCount = null;
        var remaining = PaddedSize(size) - 1;
        while (await TrySkipAsync(content, remaining, cancellationToken)
            && await TryReadExactlyAsync(content, buffer.AsMemory(0, 8), cancellationToken))
        {
            var fourCc = buffer.AsSpan(0, 4);
            size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));
            remaining = PaddedSize(size);
            if (fourCc.SequenceEqual("ANIM"u8) && size >= 6)
            {
                // Background color (4 bytes), loop count (2 bytes).
                if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 6), cancellationToken))
                {
                    break;
                }

                loopCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4, 2));
                remaining -= 6;
            }
            else if (fourCc.SequenceEqual("ANMF"u8))
            {
                frames++;
                if (frames >= limit)
                {
                    break;
                }

                // Frame X, Y, width - 1, height - 1 (3 bytes each), duration in milliseconds (3 bytes), flags.
                if (size >= 16)
                {
                    if (!await TryReadExactlyAsync(content, buffer.AsMemory(0, 16), cancellationToken))
                    {
                        break;
                    }

                    duration += TimeSpan.FromMilliseconds(buffer[12] | (buffer[13] << 8) | (buffer[14] << 16));
                    remaining -= 16;
                }
            }
        }

        return new(frames, duration, loopCount);
    }

    /// <summary>
    /// Gets the size of a chunk's payload in the file: chunks with an odd size are followed by one padding byte.
    /// </summary>
    private static long PaddedSize(uint size) => size + (size & 1);
}
