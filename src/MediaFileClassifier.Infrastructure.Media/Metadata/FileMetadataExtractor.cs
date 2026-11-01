using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// Reads nothing from the content: only the detected format and the file-level facts.
/// </summary>
/// <param name="timeProvider">Clock for the extraction time.</param>
public sealed class FileMetadataExtractor(TimeProvider timeProvider) : IMediaMetadataExtractor
{
    /// <inheritdoc />
    public Task<TechnicalMetadata> ExtractAsync(MediaContent content, MediaFormatDetection format, CancellationToken cancellationToken)
        => Task.FromResult(MetadataFactory.Create(content, format, timeProvider));
}
