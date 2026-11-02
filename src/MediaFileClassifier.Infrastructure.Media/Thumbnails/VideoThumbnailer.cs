using FFMpegCore;
using FFMpegCore.Pipes;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Metadata;
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

    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

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

    private static async Task<SKBitmap?> ReadFrameAsync(string folder, string path, TimeSpan position, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FrameTimeout);
        using var output = new MemoryStream();

        // Scale down to the decode size the image thumbnails use, never up; FFmpeg applies the rotation before the
        // filter, so the limits are on the upright frame.
        var edge = ThumbnailEncoder.SourceLongestEdge;
        await FFMpegArguments
            .FromFileInput(path, verifyExists: true, options => options.Seek(position))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithFrameOutputCount(1)
                .WithCustomArgument($"-vf \"scale='min({edge},iw)':'min({edge},ih)':force_original_aspect_ratio=decrease\"")
                .WithVideoCodec("bmp")
                .ForceFormat("image2pipe"))
            .CancellableThrough(timeout.Token)
            .ProcessAsynchronously(throwOnError: true, new FFOptions { BinaryFolder = folder })
            .ConfigureAwait(false);
        if (output.Length == 0)
        {
            return null;
        }

        using var data = SKData.CreateCopy(output.GetBuffer().AsSpan(0, (int)output.Length));
        using var codec = SkiaDecoding.OpenCodec(data);
        return SkiaDecoding.DecodeStill(codec, edge, edge);
    }

    private async Task<SKBitmap> ReadRepresentativeFrameAsync(MediaContent content, string folder, TimeSpan? duration, CancellationToken cancellationToken)
    {
        await using var file = await LocalCopy.CreateAsync(content, cancellationToken).ConfigureAwait(false);
        SKBitmap? best = null;
        var bestLuma = double.MinValue;
        Exception? lastError = null;
        foreach (var position in GetPositions(duration))
        {
            SKBitmap? frame;
            try
            {
                frame = await ReadFrameAsync(folder, file.Path, position, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// A file FFmpeg can open by path: the item's own file, or a temporary copy of a content stream, since FFmpeg
    /// needs to seek in an MP4.
    /// </summary>
    private sealed class LocalCopy : IAsyncDisposable
    {
        private readonly bool isTemporary;

        private LocalCopy(string path, bool isTemporary)
        {
            Path = path;
            this.isTemporary = isTemporary;
        }

        public string Path { get; }

        public static async Task<LocalCopy> CreateAsync(MediaContent content, CancellationToken cancellationToken)
        {
            if (content.LocalPath is { } path)
            {
                return new(path, isTemporary: false);
            }

            var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mfc-video-{Guid.NewGuid():N}.mp4");
            try
            {
                var source = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    var target = File.Create(temporary);
                    await using (target.ConfigureAwait(false))
                    {
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                }

                return new(temporary, isTemporary: true);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (isTemporary)
            {
                File.Delete(Path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
