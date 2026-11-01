using System.Buffers;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// Reading helpers for the structure readers, which must cope with truncated files and streams that can't seek.
/// </summary>
internal static class StreamReading
{
    /// <summary>
    /// Fills the buffer.
    /// </summary>
    /// <returns><see langword="false"/> when the stream ended first.</returns>
    public static async ValueTask<bool> TryReadExactlyAsync(Stream content, Memory<byte> buffer, CancellationToken cancellationToken)
        => await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken) == buffer.Length;

    /// <summary>
    /// Moves forward: seeks when the stream can, and reads and discards otherwise.
    /// </summary>
    /// <returns><see langword="false"/> when the stream ended first.</returns>
    public static async ValueTask<bool> TrySkipAsync(Stream content, long count, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            if (content.Length - content.Position < count)
            {
                content.Seek(0, SeekOrigin.End);
                return false;
            }

            content.Seek(count, SeekOrigin.Current);
            return true;
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(count, 81920));
        try
        {
            while (count > 0)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), cancellationToken);
                if (read == 0)
                {
                    return false;
                }

                count -= read;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
