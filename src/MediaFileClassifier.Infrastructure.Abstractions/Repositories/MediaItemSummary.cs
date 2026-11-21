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
/// <param name="IsFavorite">Whether the user marked the item as a favorite (E11-S01).</param>
/// <param name="DateTaken">When the photo or video was taken, as local wall-clock time, once its metadata is read.</param>
/// <param name="Width">Width as displayed (orientation applied), once its metadata is read.</param>
/// <param name="Height">Height as displayed (orientation applied), once its metadata is read.</param>
/// <param name="Folder">The folder the file is in: the indexed folder's path and the file's subfolders.</param>
/// <param name="IsFolderOffline">Whether the item's indexed folder is Offline (D025), e.g. on an unplugged drive.</param>
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
    bool ThumbnailFailed,
    bool IsFavorite = false,
    DateTime? DateTaken = null,
    int? Width = null,
    int? Height = null,
    string? Folder = null,
    bool IsFolderOffline = false);
