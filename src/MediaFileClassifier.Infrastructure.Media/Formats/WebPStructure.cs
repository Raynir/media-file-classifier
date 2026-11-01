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
    {
        var buffer = new byte[RiffHeaderLength];
        if (!await TryReadExactlyAsync(content, buffer, cancellationToken)
            || !await TryReadExactlyAsync(content, buffer.AsMemory(0, 8), cancellationToken))
        {
            return 0;
        }

        // The first chunk tells the layout: only an extended (VP8X) file can be animated, and its flags say whether it is.
        if (!buffer.AsSpan(0, 4).SequenceEqual("VP8X"u8))
        {
            return 1;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));
        if (size == 0 || !await TryReadExactlyAsync(content, buffer.AsMemory(0, 1), cancellationToken))
        {
            return 0;
        }

        if ((buffer[0] & AnimationFlag) == 0)
        {
            return 1;
        }

        var frames = 0;
        var remaining = PaddedSize(size) - 1;
        while (await TrySkipAsync(content, remaining, cancellationToken)
            && await TryReadExactlyAsync(content, buffer.AsMemory(0, 8), cancellationToken))
        {
            if (buffer.AsSpan(0, 4).SequenceEqual("ANMF"u8) && ++frames >= limit)
            {
                return frames;
            }

            remaining = PaddedSize(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)));
        }

        return frames;
    }

    /// <summary>
    /// Gets the size of a chunk's payload in the file: chunks with an odd size are followed by one padding byte.
    /// </summary>
    private static long PaddedSize(uint size) => size + (size & 1);
}
