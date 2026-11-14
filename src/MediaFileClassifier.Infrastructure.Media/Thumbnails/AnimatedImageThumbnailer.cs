using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Makes the thumbnails of GIF and WebP files (E03-S04-T02). An animated file shows its first frame that isn't one
/// flat colour within the first <see cref="FramesToTry"/> frames, so an empty first frame doesn't become the
/// thumbnail; when all of them are flat, the first frame. A still file is handled like any other image.
/// </summary>
/// <param name="stills">Makes the thumbnails of still files.</param>
/// <param name="logger">Logger.</param>
public sealed partial class AnimatedImageThumbnailer(ImageThumbnailer stills, ILogger<AnimatedImageThumbnailer> logger) : IThumbnailGenerator
{
    /// <summary>
    /// How many leading frames are tried.
    /// </summary>
    public const int FramesToTry = 10;

    /// <inheritdoc />
    public async Task<ThumbnailImages> GenerateAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken)
    {
        if (format.MediaType is not MediaType.AnimatedImage)
        {
            return await stills.GenerateAsync(content, format, metadata, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var data = await SkiaDecoding.ReadAsync(content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var codec = SkiaDecoding.OpenCodec(data);
            using var upright = OrientationTransform.Apply(DecodeRepresentativeFrame(codec, cancellationToken), metadata.Orientation);
            return new() { Images = ThumbnailEncoder.Encode(upright) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, content.ItemId.Value, ex);
            return ThumbnailImages.Failed(new(ThumbnailWarnings.ThumbnailFailed, ex.Message));
        }
    }

    /// <summary>
    /// Decodes frames in order onto one canvas, as they are shown, until one isn't a flat colour.
    /// </summary>
    internal static SKBitmap DecodeRepresentativeFrame(SKCodec codec, CancellationToken cancellationToken)
    {
        var info = SkiaDecoding.TargetInfo(codec.Info.Size);
        var frames = codec.FrameInfo;
        using var canvas = new SKBitmap(info);
        SKBitmap? first = null;
        for (var index = 0; index < Math.Clamp(frames.Length, 1, FramesToTry); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodeFrame(codec, canvas, index, frames.Length == 0 ? -1 : frames[index].RequiredFrame);
            if (!FrameStatistics.Measure(canvas).IsUniform)
            {
                first?.Dispose();
                return canvas.Copy();
            }

            first ??= canvas.Copy();
        }

        return first!;
    }

    /// <summary>
    /// Decodes a frame onto the canvas, which holds the previous frame. A frame that builds on an earlier one is drawn
    /// over the canvas; if the codec can't use the canvas for that, it decodes the earlier frames itself.
    /// </summary>
    internal static void DecodeFrame(SKCodec codec, SKBitmap canvas, int index, int requiredFrame)
    {
        if (requiredFrame < 0)
        {
            canvas.Erase(SKColors.Transparent);
            SkiaDecoding.Check(codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(index)));
            return;
        }

        var result = codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(index, index - 1));
        if (result is SKCodecResult.InvalidParameters)
        {
            result = codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(index));
        }

        SkiaDecoding.Check(result);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't make the thumbnail of animated item {ItemId}")]
    private static partial void LogFailed(ILogger logger, Guid itemId, Exception exception);
}
