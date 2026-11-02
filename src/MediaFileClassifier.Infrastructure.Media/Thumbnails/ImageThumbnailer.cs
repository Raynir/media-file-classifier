using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Makes the thumbnails of still images (E03-S04-T01): decodes with SkiaSharp, turns the picture upright by its EXIF
/// orientation, scales it with high-quality resampling and encodes WebP.
/// </summary>
/// <param name="logger">Logger.</param>
public sealed partial class ImageThumbnailer(ILogger<ImageThumbnailer> logger) : IThumbnailGenerator
{
    /// <inheritdoc />
    public async Task<ThumbnailImages> GenerateAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken)
    {
        try
        {
            using var data = await SkiaDecoding.ReadAsync(content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var codec = SkiaDecoding.OpenCodec(data);
            using var upright = OrientationTransform.Apply(SkiaDecoding.DecodeStill(codec, ThumbnailEncoder.SourceLongestEdge, (int)ThumbnailSize.Large), metadata.Orientation);
            return new() { Images = ThumbnailEncoder.Encode(upright) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A file that can't be decoded must not fail the item (E03-S04): it gets a placeholder and a warning.
            LogFailed(logger, content.ItemId.Value, ex);
            return ThumbnailImages.Failed(new(ThumbnailWarnings.ThumbnailFailed, ex.Message));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't make the thumbnail of item {ItemId}")]
    private static partial void LogFailed(ILogger logger, Guid itemId, Exception exception);
}
