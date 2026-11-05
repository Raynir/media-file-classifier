using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Video;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Frames;

/// <summary>
/// Samples the keyframes of videos and animated images (E06-S02-T02, T03). Video frames come from FFmpeg, one exact
/// seek per position, so the cost doesn't grow with the duration; GIF and WebP frames are decoded with SkiaSharp and
/// sampled by display time. Without FFmpeg a video gets no frames and
/// <see cref="FrameSamplingWarnings.FrameSamplingFfmpegUnavailable"/>.
/// </summary>
/// <param name="capabilities">Tells whether FFmpeg was found at startup.</param>
/// <param name="locator">Finds <c>ffmpeg</c>.</param>
/// <param name="hasher">Hashes the frames, for near-duplicate removal.</param>
/// <param name="logger">Logger.</param>
public sealed partial class FrameSampler(
    ICapabilityService capabilities,
    FfprobeLocator locator,
    IPerceptualHasher hasher,
    ILogger<FrameSampler> logger) : IFrameSampler
{
    /// <summary>
    /// After this many FFmpeg timeouts in a row, the remaining positions of a video are skipped.
    /// </summary>
    internal const int MaxConsecutiveTimeouts = 2;

    /// <inheritdoc />
    public async Task<FrameSample> SampleAsync(
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        FrameSamplingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (format.MediaType is not (MediaType.Video or MediaType.AnimatedImage))
        {
            throw new ArgumentException($"Frames are sampled from videos and animated images, not {format.MediaType}.", nameof(format));
        }

        try
        {
            return format.MediaType is MediaType.Video
                ? await SampleVideoAsync(content, metadata, options, cancellationToken).ConfigureAwait(false)
                : await SampleAnimationAsync(content, metadata, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, content.ItemId.Value, ex);
            return FrameSample.Failed(new(FrameSamplingWarnings.FrameSamplingFailed, ex.Message));
        }
    }

    private async Task<FrameSample> SampleVideoAsync(MediaContent content, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken)
    {
        var folder = capabilities.Get(KnownCapabilities.VideoAnalysis).IsAvailable ? locator.FindFfmpegFolder() : null;
        if (folder is null)
        {
            return FrameSample.Failed(new(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable));
        }

        await using var file = await LocalMediaFile.CreateAsync(content, cancellationToken).ConfigureAwait(false);
        var selector = new FrameSelector<Candidate>(options);
        Exception? lastError = null;
        var timeouts = 0;
        foreach (var position in FramePositions.ForVideo(metadata.Duration, options))
        {
            SKBitmap? frame;
            try
            {
                frame = await FfmpegFrameReader.ReadFrameAsync(folder, file.Path, position, options.MaxLongestEdge, cancellationToken).ConfigureAwait(false);
                timeouts = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unreadable position, e.g. a damaged stretch, doesn't lose the others. A file that makes FFmpeg
                // hang twice in a row is given up on, so it doesn't hold a worker for every position's timeout.
                lastError = ex;
                if (ex is OperationCanceledException && ++timeouts >= MaxConsecutiveTimeouts)
                {
                    break;
                }

                continue;
            }

            if (frame is not null)
            {
                using (frame)
                {
                    Offer(selector, frame, position, frameNumber: null);
                }
            }
        }

        if (selector.Offered == 0)
        {
            return FrameSample.Failed(new(FrameSamplingWarnings.FrameSamplingFailed, lastError?.Message ?? "FFmpeg read no frame."));
        }

        return Result(selector);
    }

    private async Task<FrameSample> SampleAnimationAsync(MediaContent content, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken)
    {
        using var data = await SkiaDecoding.ReadAsync(content, cancellationToken).ConfigureAwait(false);
        using var codec = SkiaDecoding.OpenCodec(data);
        var frames = codec.FrameInfo;
        var delays = frames.Select(frame => TimeSpan.FromMilliseconds(frame.Duration)).ToArray();
        var picks = FramePositions.ForAnimation(delays, options);
        var selector = new FrameSelector<Candidate>(options);

        // Frames build on earlier ones, so they are decoded in order onto one canvas, as they are shown.
        using var canvas = new SKBitmap(SkiaDecoding.TargetInfo(codec.Info.Size));
        var next = 0;
        for (var frame = 0; frame <= picks[^1]; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnimatedImageThumbnailer.DecodeFrame(codec, canvas, frame, frames.Length == 0 ? -1 : frames[frame].RequiredFrame);
            if (frame != picks[next])
            {
                continue;
            }

            next++;
            var copy = canvas.Copy() ?? throw new InvalidOperationException("Not enough memory to copy the frame.");
            using var upright = OrientationTransform.Apply(copy, metadata.Orientation);
            using var scaled = ThumbnailEncoder.ScaleDown(upright, options.MaxLongestEdge);
            Offer(selector, scaled ?? upright, FramePositions.StartOf(delays, frame), frame);
        }

        return Result(selector);
    }

    private void Offer(FrameSelector<Candidate> selector, SKBitmap frame, TimeSpan timestamp, int? frameNumber)
    {
        // Composited over white, as the perceptual hash sees transparency, so the analyzers get opaque pixels.
        using var flat = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(frame, 0, 0);
        }

        var pixels = flat.GetPixelSpan().ToArray();
        var hash = hasher.ComputeFromPixels(pixels, flat.Width, flat.Height);
        var statistics = FrameStatistics.Measure(flat);
        selector.Offer(new Candidate(timestamp, frameNumber, hash, flat.Width, flat.Height, pixels), hash, statistics.IsBlankVideoFrame, statistics.MeanLuma);
    }

    private static FrameSample Result(FrameSelector<Candidate> selector) => new()
    {
        FramesRead = selector.Offered,
        Frames = [.. selector.Kept.Select((candidate, index) => new SampledFrame
        {
            Index = index,
            Timestamp = candidate.Timestamp,
            FrameNumber = candidate.FrameNumber,
            PerceptualHash = candidate.Hash,
            Width = candidate.Width,
            Height = candidate.Height,
            Pixels = candidate.Pixels,
        })],
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't sample the frames of item {ItemId}")]
    private static partial void LogFailed(ILogger logger, Guid itemId, Exception exception);

    private sealed record Candidate(TimeSpan Timestamp, int? FrameNumber, ulong Hash, int Width, int Height, byte[] Pixels);
}
