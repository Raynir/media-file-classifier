using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// The file-level metadata every extractor starts from: the detected format and the extraction time.
/// </summary>
internal static class MetadataFactory
{
    public static TechnicalMetadata Create(MediaContent content, MediaFormatDetection format, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (format.Handler is not { } handler || !format.IsSupported)
        {
            throw new ArgumentException("Metadata is read only from supported files.", nameof(format));
        }

        return new TechnicalMetadata
        {
            ItemId = content.ItemId,
            ContainerFormat = handler.FormatId,
            MimeType = handler.MimeType,
            ExtractedAt = timeProvider.GetUtcNow(),
        };
    }
}
