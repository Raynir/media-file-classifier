using System.Buffers.Binary;
using System.Text;
using static MediaFileClassifier.Infrastructure.Media.Formats.StreamReading;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// Reads the brand and the tracks of an ISO base media file (MP4) from its <c>ftyp</c> and <c>moov</c> boxes, skipping
/// the media data (ISO/IEC 14496-12).
/// </summary>
internal static class Mp4Structure
{
    /// <summary>
    /// The largest <c>moov</c> box read into memory. A two-hour video has a few megabytes.
    /// </summary>
    private const long MaxMovieBoxSize = 64 * 1024 * 1024;

    /// <summary>
    /// Reads the top-level boxes up to the <c>moov</c> box, which can come before or after the media data.
    /// </summary>
    /// <param name="content">The file, positioned at the start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was found; <see cref="Mp4Summary.Tracks"/> is <c>null</c> when there is no readable <c>moov</c> box.</returns>
    public static async ValueTask<Mp4Summary> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        string? majorBrand = null;
        var header = new byte[16];
        while (await TryReadExactlyAsync(content, header.AsMemory(0, 8), cancellationToken))
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = 8;
            if (size == 1)
            {
                if (!await TryReadExactlyAsync(content, header.AsMemory(8, 8), cancellationToken))
                {
                    break;
                }

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)), long.MaxValue);
                headerSize = 16;
            }
            else if (size == 0)
            {
                // The last box, running to the end of the file.
                size = content.CanSeek ? content.Length - content.Position + headerSize : long.MaxValue;
            }

            var payloadSize = size - headerSize;
            if (payloadSize < 0)
            {
                return new(majorBrand, null, "box size smaller than its header");
            }

            var type = header.AsSpan(4, 4);
            if (type.SequenceEqual("ftyp"u8) || type.SequenceEqual("moov"u8))
            {
                if (payloadSize > MaxMovieBoxSize)
                {
                    return new(majorBrand, null, $"{FourCc(type)} box too large");
                }

                var isMovie = type.SequenceEqual("moov"u8);
                var payload = new byte[payloadSize];
                if (!await TryReadExactlyAsync(content, payload, cancellationToken))
                {
                    break;
                }

                if (isMovie)
                {
                    return new(majorBrand, ReadTracks(payload), null);
                }

                majorBrand = payload.Length >= 4 ? FourCc(payload.AsSpan(0, 4)) : null;
            }
            else if (!await TrySkipAsync(content, payloadSize, cancellationToken))
            {
                break;
            }
        }

        return new(majorBrand, null, "no moov box");
    }

    private static List<Mp4Track> ReadTracks(ReadOnlySpan<byte> movie)
    {
        var tracks = new List<Mp4Track>();
        var offset = 0;
        while (TryReadBox(movie, ref offset, out var type, out var track))
        {
            if (!type.SequenceEqual("trak"u8) || !TryFindChild(track, "mdia"u8, out var media))
            {
                continue;
            }

            // hdlr: version and flags (4 bytes), pre_defined (4 bytes), handler_type.
            var handlerType = TryFindChild(media, "hdlr"u8, out var handler) && handler.Length >= 12
                ? FourCc(handler[8..12])
                : null;

            // stsd: version and flags (4 bytes), entry_count (4 bytes), then the first sample entry's size and format.
            var sampleFormat = TryFindChild(media, "minf"u8, out var mediaInfo)
                && TryFindChild(mediaInfo, "stbl"u8, out var sampleTable)
                && TryFindChild(sampleTable, "stsd"u8, out var sampleDescription)
                && sampleDescription.Length >= 16
                    ? FourCc(sampleDescription[12..16])
                    : null;
            tracks.Add(new(handlerType, sampleFormat));
        }

        return tracks;
    }

    private static bool TryFindChild(ReadOnlySpan<byte> container, ReadOnlySpan<byte> wanted, out ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        while (TryReadBox(container, ref offset, out var type, out payload))
        {
            if (type.SequenceEqual(wanted))
            {
                return true;
            }
        }

        payload = default;
        return false;
    }

    /// <summary>
    /// Reads the box at <paramref name="offset"/> of an in-memory container and moves past it.
    /// </summary>
    /// <returns><see langword="false"/> at the end of the container or at a damaged box.</returns>
    private static bool TryReadBox(ReadOnlySpan<byte> container, scoped ref int offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> payload)
    {
        type = default;
        payload = default;
        var remaining = container[offset..];
        if (remaining.Length < 8)
        {
            return false;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(remaining);
        var headerSize = 8;
        if (size == 1)
        {
            if (remaining.Length < 16)
            {
                return false;
            }

            size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]), long.MaxValue);
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = remaining.Length;
        }

        if (size < headerSize || size > remaining.Length)
        {
            return false;
        }

        type = remaining[4..8];
        payload = remaining[headerSize..(int)size];
        offset += (int)size;
        return true;
    }

    private static string FourCc(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}

/// <summary>
/// The parts of an MP4 file that tell what it holds.
/// </summary>
/// <param name="MajorBrand">The <c>ftyp</c> major brand, such as <c>isom</c>, or <c>null</c> without one.</param>
/// <param name="Tracks">The tracks, or <c>null</c> when the <c>moov</c> box is missing or unreadable.</param>
/// <param name="Problem">What is wrong when <paramref name="Tracks"/> is <c>null</c>.</param>
internal sealed record Mp4Summary(string? MajorBrand, IReadOnlyList<Mp4Track>? Tracks, string? Problem);

/// <summary>
/// One track of an MP4 file.
/// </summary>
/// <param name="HandlerType">The handler type, such as <c>vide</c> or <c>soun</c>.</param>
/// <param name="SampleFormat">The first sample entry's format, the codec, such as <c>avc1</c>, <c>hvc1</c> or <c>av01</c>.</param>
internal sealed record Mp4Track(string? HandlerType, string? SampleFormat);
