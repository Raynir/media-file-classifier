using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Abstractions.Metadata;

/// <summary>
/// Reads the technical metadata of one kind of file (E03-S03): dimensions, dates, camera, GPS, codecs. Each
/// <see cref="IMediaFormatHandler"/> names its extractor.
/// </summary>
public interface IMediaMetadataExtractor
{
    /// <summary>
    /// Reads a file's technical metadata. A broken or missing value never fails the item: the result keeps what could
    /// be read and lists the problems in <see cref="TechnicalMetadata.Warnings"/>. The date taken falls back from the
    /// file's own date to its modification time.
    /// </summary>
    /// <param name="content">The file to read.</param>
    /// <param name="format">The file's detected format, which must be supported.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata.</returns>
    Task<TechnicalMetadata> ExtractAsync(MediaContent content, MediaFormatDetection format, CancellationToken cancellationToken);
}
