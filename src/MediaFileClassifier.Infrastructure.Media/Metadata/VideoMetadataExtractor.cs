using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FFMpegCore;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// Reads video metadata with FFprobe, through FFMpegCore (D068): duration, resolution, frame rate, codecs, the audio
/// track, the container's creation time, and the display rotation as an orientation, so a portrait phone video stored
/// landscape reports portrait display dimensions. Without FFmpeg (the Video analysis capability is off, E01-S07-T03) a video
/// keeps its file-level metadata and an <see cref="MetadataWarnings.FfmpegUnavailable"/> warning.
/// </summary>
/// <param name="capabilities">Capability flags, for Video analysis.</param>
/// <param name="ffprobe">Finds the <c>ffprobe</c> executable.</param>
/// <param name="timeProvider">Clock for the extraction time.</param>
/// <param name="logger">Logger.</param>
public sealed partial class VideoMetadataExtractor(
    ICapabilityService capabilities,
    FfprobeLocator ffprobe,
    TimeProvider timeProvider,
    ILogger<VideoMetadataExtractor> logger) : IMediaMetadataExtractor
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc />
    public async Task<TechnicalMetadata> ExtractAsync(MediaContent content, MediaFormatDetection format, CancellationToken cancellationToken)
        => DateTakenFallback.Apply(await ReadAsync(content, format, cancellationToken), content.LastModifiedUtc);

    private async Task<TechnicalMetadata> ReadAsync(MediaContent content, MediaFormatDetection format, CancellationToken cancellationToken)
    {
        var metadata = MetadataFactory.Create(content, format, timeProvider);
        var folder = capabilities.Get(KnownCapabilities.VideoAnalysis).IsAvailable ? ffprobe.FindFolder() : null;
        if (folder is null)
        {
            return metadata with { Warnings = [new(MetadataWarnings.FfmpegUnavailable)] };
        }

        IMediaAnalysis analysis;
        try
        {
            analysis = await AnalyseAsync(content, folder, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // FFprobe failing on one file must not fail the item (E03-S03): log and keep the file-level metadata.
            LogProbeFailed(logger, content.ItemId.Value, ex);
            return metadata with { Warnings = [new(MetadataWarnings.VideoProbeFailed, ex.Message)] };
        }

        var video = analysis.PrimaryVideoStream;
        var audio = analysis.PrimaryAudioStream;
        var creationTime = ReadCreationTime(analysis.Format.Tags);

        // FFprobe can finish without an error and still find nothing, e.g. "moov atom not found" on a truncated file.
        IReadOnlyList<ExtractionWarning> warnings = video is null
            ? [new(MetadataWarnings.VideoProbeFailed, analysis.ErrorData.LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "No video stream")]
            : [];
        return metadata with
        {
            Width = video is { Width: > 0 } ? video.Width : null,
            Height = video is { Height: > 0 } ? video.Height : null,
            Orientation = video is null ? ImageOrientation.Normal : ToOrientation(video.Rotation),
            Duration = analysis.Duration > TimeSpan.Zero ? analysis.Duration : null,
            FrameRate = video is not null && double.IsFinite(video.AvgFrameRate) && video.AvgFrameRate > 0 ? video.AvgFrameRate : null,
            VideoCodec = video?.CodecName,
            AudioCodec = audio?.CodecName,
            HasAudio = analysis.AudioStreams.Count > 0,
            BitRate = analysis.Format.BitRate > 0 ? (long)analysis.Format.BitRate : null,
            ColorSpace = video?.ColorSpace is { Length: > 0 } colorSpace && colorSpace != "unknown" ? colorSpace : null,
            DateTaken = creationTime is { } instant ? DateTakenFallback.ToWallClock(instant) : null,
            DateTakenOffset = creationTime is null ? null : TimeSpan.Zero,
            DateTakenSource = creationTime is null ? null : DateTakenSource.VideoCreationTime,
            RawMetadataJson = ToRawJson(analysis),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Maps FFprobe's display rotation, in degrees counter-clockwise (a phone held upright records −90), to the EXIF-style
    /// orientation. Angles that aren't a quarter turn count as none.
    /// </summary>
    /// <param name="counterClockwiseDegrees">The rotation FFprobe reports.</param>
    /// <returns>The orientation.</returns>
    internal static ImageOrientation ToOrientation(int counterClockwiseDegrees)
        => (((-counterClockwiseDegrees % 360) + 360) % 360) switch
        {
            90 => ImageOrientation.Rotate90,
            180 => ImageOrientation.Rotate180,
            270 => ImageOrientation.Rotate270,
            _ => ImageOrientation.Normal,
        };

    /// <summary>
    /// Runs FFprobe on the file's path when it has one, since an MP4 with its index at the end can't be read from a
    /// pipe; otherwise on the stream.
    /// </summary>
    private static async Task<IMediaAnalysis> AnalyseAsync(MediaContent content, string folder, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var options = new FFOptions { BinaryFolder = folder };
        if (content.LocalPath is { } path)
        {
            return await FFProbe.AnalyseAsync(path, options, timeout.Token);
        }

        await using var stream = await content.OpenReadAsync(cancellationToken);
        return await FFProbe.AnalyseAsync(stream, options, timeout.Token);
    }

    /// <summary>
    /// Reads the container's <c>creation_time</c>, an ISO 8601 UTC instant. Muxers write 1904-01-01 or 1970-01-01 when
    /// they don't know it, so dates before 1971 are ignored.
    /// </summary>
    private static DateTimeOffset? ReadCreationTime(IReadOnlyDictionary<string, string>? tags)
        => tags is not null
            && tags.TryGetValue("creation_time", out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            && value.Year > 1970
                ? value
                : null;

    /// <summary>
    /// Writes what FFprobe reported, without the file name, so the dump holds no path.
    /// </summary>
    private static string ToRawJson(IMediaAnalysis analysis)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("format");
            writer.WriteString("format_name", analysis.Format.FormatName);
            writer.WriteNumber("duration_seconds", analysis.Format.Duration.TotalSeconds);
            writer.WriteNumber("bit_rate", analysis.Format.BitRate);
            WriteTags(writer, analysis.Format.Tags);
            writer.WriteEndObject();
            writer.WriteStartArray("streams");
            foreach (var video in analysis.VideoStreams)
            {
                writer.WriteStartObject();
                WriteStream(writer, "video", video);
                writer.WriteNumber("width", video.Width);
                writer.WriteNumber("height", video.Height);
                writer.WriteNumber("avg_frame_rate", double.IsFinite(video.AvgFrameRate) ? video.AvgFrameRate : 0);
                writer.WriteNumber("rotation", video.Rotation);
                writer.WriteString("pix_fmt", video.PixelFormat);
                writer.WriteString("profile", video.Profile);
                writer.WriteString("color_space", video.ColorSpace);
                writer.WriteString("color_primaries", video.ColorPrimaries);
                writer.WriteString("color_transfer", video.ColorTransfer);
                WriteTags(writer, video.Tags);
                writer.WriteEndObject();
            }

            foreach (var audio in analysis.AudioStreams)
            {
                writer.WriteStartObject();
                WriteStream(writer, "audio", audio);
                writer.WriteNumber("channels", audio.Channels);
                writer.WriteString("channel_layout", audio.ChannelLayout);
                writer.WriteNumber("sample_rate", audio.SampleRateHz);
                WriteTags(writer, audio.Tags);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteStream(Utf8JsonWriter writer, string type, MediaStream mediaStream)
    {
        writer.WriteString("codec_type", type);
        writer.WriteNumber("index", mediaStream.Index);
        writer.WriteString("codec_name", mediaStream.CodecName);
        writer.WriteString("codec_tag", mediaStream.CodecTagString);
        writer.WriteNumber("bit_rate", mediaStream.BitRate);
        writer.WriteNumber("duration_seconds", mediaStream.Duration.TotalSeconds);
    }

    private static void WriteTags(Utf8JsonWriter writer, IReadOnlyDictionary<string, string>? tags)
    {
        writer.WriteStartObject("tags");
        foreach (var (key, value) in tags ?? new Dictionary<string, string>())
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FFprobe couldn't read the video of item {ItemId}")]
    private static partial void LogProbeFailed(ILogger logger, Guid itemId, Exception exception);
}
