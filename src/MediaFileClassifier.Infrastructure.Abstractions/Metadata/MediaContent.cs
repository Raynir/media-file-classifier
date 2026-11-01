using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Metadata;

/// <summary>
/// A file whose metadata is read: how to open it, and the file-level facts the extractors fall back on.
/// </summary>
/// <param name="ItemId">The item the file belongs to.</param>
/// <param name="LastModifiedUtc">The file's last write time, the last fallback for the date taken.</param>
/// <param name="OpenReadAsync">Opens the file for reading; an extractor may open it more than once. The caller of the
/// function disposes the stream. On desktop it comes from <c>IMediaFileSource.OpenReadAsync</c>.</param>
public sealed record MediaContent(
    Id<MediaItem> ItemId,
    DateTimeOffset LastModifiedUtc,
    Func<CancellationToken, Task<Stream>> OpenReadAsync)
{
    /// <summary>
    /// Gets the file's path on platforms where it has one (desktop), or <c>null</c>. Tools that need a seekable file,
    /// such as FFprobe, use it; without it they read the stream.
    /// </summary>
    public string? LocalPath { get; init; }
}
