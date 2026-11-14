using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// A thumbnail cache with nothing in it.
/// </summary>
internal sealed class NoThumbnails : IThumbnailCache
{
    public string? Find(LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt) => null;

    public Task<ThumbnailOutcome> GenerateAsync(LibraryLayout library, MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public void Invalidate(LibraryLayout library, Id<MediaItem> itemId)
    {
    }
}
