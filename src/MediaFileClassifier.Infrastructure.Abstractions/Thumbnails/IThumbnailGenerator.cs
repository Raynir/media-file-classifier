using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// Makes the thumbnails of one media format (E03-S04). Each <see cref="IMediaFormatHandler"/> owns one (D098).
/// </summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// Makes an item's thumbnails in every <see cref="ThumbnailSize"/>, upright. Never throws for a bad file: a
    /// failure gives a result without images and a warning, so the item is still registered with a placeholder.
    /// </summary>
    /// <param name="content">The file.</param>
    /// <param name="format">The detected format.</param>
    /// <param name="metadata">The item's technical metadata, for the orientation and the video duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encoded thumbnails, or the warning that explains why there are none.</returns>
    Task<ThumbnailImages> GenerateAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken);
}
