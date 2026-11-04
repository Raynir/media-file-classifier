using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Infrastructure.Media.Video;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Makes the thumbnails of videos with the local FFmpeg (E03-S04-T03, D068). Reads the frame at 10% of the duration,
/// past most intros; if it is blank (nearly black or one flat colour) it tries 25%, 50% and then the first frame, and
/// keeps the brightest when all are blank. FFmpeg turns the frame upright by the display rotation itself. Without
/// FFmpeg the item gets a placeholder and <see cref="ThumbnailWarnings.ThumbnailFfmpegUnavailable"/>, never a failure.
/// </summary>
/// <param name="capabilities">Tells whether FFmpeg was found at startup.</param>
/// <param name="locator">Finds <c>ffmpeg</c>.</param>
/// <param name="logger">Logger.</param>
public sealed partial class VideoThumbnailer(ICapabilityService capabilities, FfprobeLocator locator, ILogger<VideoThumbnailer> logger) : IThumbnailGenerator
{
    /// <summary>
    /// The positions tried, as fractions of the duration. The first frame comes last.
    /// </summary>
    internal static readonly double[] Positions = [0.10, 0.25, 0.50];

    /// <inheritdoc />
    public async Task<ThumbnailImages> GenerateAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken)
    {
        var folder = capabilities.Get(KnownCapabilities.VideoAnalysis).IsAvailable ? locator.FindFfmpegFolder() : null;
        if (folder is null)
        {
            return ThumbnailImages.Failed(new(ThumbnailWarnings.ThumbnailFfmpegUnavailable));
        }

        try
        {
            using var frame = await ReadRepresentativeFrameAsync(content, folder, metadata.Duration, cancellationToken).ConfigureAwait(false);
            return new() { Images = ThumbnailEncoder.Encode(frame) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A video FFmpeg can't read must not fail the item (E03-S04): it gets a placeholder and a warning.
            LogFailed(logger, content.ItemId.Value, ex);
            return ThumbnailImages.Failed(new(ThumbnailWarnings.ThumbnailFailed, ex.Message));
        }
    }

    /// <summary>
    /// Gets the positions to try: <see cref="Positions"/> of the duration, then the start. Only the start when the
    /// duration is unknown.
    /// </summary>
    /// <param name="duration">The video's duration.</param>
    /// <returns>The positions, in order.</returns>
    internal static IEnumerable<TimeSpan> GetPositions(TimeSpan? duration)
    {
        if (duration is { } length && length > TimeSpan.Zero)
        {
            foreach (var fraction in Positions)
            {
                yield return length * fraction;
            }
        }

        yield return TimeSpan.Zero;
    }

    private async Task<SKBitmap> ReadRepresentativeFrameAsync(MediaContent content, string folder, TimeSpan? duration, CancellationToken cancellationToken)
    {
        await using var file = await LocalMediaFile.CreateAsync(content, cancellationToken).ConfigureAwait(false);
        SKBitmap? best = null;
        var bestLuma = double.MinValue;
        Exception? lastError = null;
        foreach (var position in GetPositions(duration))
        {
            SKBitmap? frame;
            try
            {
                frame = await FfmpegFrameReader.ReadFrameAsync(folder, file.Path, position, ThumbnailEncoder.SourceLongestEdge, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                continue;
            }

            if (frame is null)
            {
                continue;
            }

            var statistics = FrameStatistics.Measure(frame);
            if (!statistics.IsBlankVideoFrame)
            {
                best?.Dispose();
                return frame;
            }

            LogBlankFrame(logger, content.ItemId.Value, position);
            if (statistics.MeanLuma > bestLuma)
            {
                best?.Dispose();
                best = frame;
                bestLuma = statistics.MeanLuma;
            }
            else
            {
                frame.Dispose();
            }
        }

        return best ?? throw new InvalidDataException(lastError?.Message ?? "FFmpeg read no frame.", lastError);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't make the thumbnail of video item {ItemId}")]
    private static partial void LogFailed(ILogger logger, Guid itemId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The frame of video item {ItemId} at {Position} is blank")]
    private static partial void LogBlankFrame(ILogger logger, Guid itemId, TimeSpan position);
}
