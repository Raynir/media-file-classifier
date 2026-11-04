using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// What a media browser tile shows of an item (E14-S01), read in one query without loading the whole item.
/// </summary>
/// <param name="Id">Item ID.</param>
/// <param name="FileName">File name with its extension.</param>
/// <param name="FileFormat">Lower-case extension without the dot.</param>
/// <param name="MediaType">Kind of media.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="FileModifiedAt">File modification time; a thumbnail written before it is stale.</param>
/// <param name="Status">Processing status.</param>
/// <param name="StatusReason">Error message or other reason for the status, if any.</param>
/// <param name="Duration">Playback length of a video or animated image, once its metadata is read.</param>
/// <param name="ThumbnailFailed">Whether making the thumbnail failed (D128), so the cell shows the failed placeholder.</param>
public sealed record MediaItemSummary(
    Id<MediaItem> Id,
    string FileName,
    string FileFormat,
    MediaType MediaType,
    long SizeBytes,
    DateTimeOffset FileModifiedAt,
    MediaItemStatus Status,
    string? StatusReason,
    TimeSpan? Duration,
    bool ThumbnailFailed);
