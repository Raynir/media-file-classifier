using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// Structured filters (spec 1.7) and the navigation scope (E13-S02). Every condition that's set must hold; a <c>null</c>
/// condition doesn't filter. Items Excluded by their folder's rules or by the user never match (D048, D024).
/// </summary>
public sealed record SearchFilter
{
    /// <summary>
    /// Gets the filter that keeps every item that isn't Excluded.
    /// </summary>
    public static SearchFilter None { get; } = new();

    /// <summary>
    /// Gets the media types to keep, or <c>null</c> for all.
    /// </summary>
    public IReadOnlyCollection<MediaType>? MediaTypes { get; init; }

    /// <summary>
    /// Gets the file formats to keep, as extensions without the dot, ignoring case (e.g. <c>jpg</c>, <c>mp4</c>), or
    /// <c>null</c> for all.
    /// </summary>
    public IReadOnlyCollection<string>? Formats { get; init; }

    /// <summary>
    /// Gets the earliest item date to keep, or <c>null</c>. The item date is the date taken, or the file modification
    /// time if there's none. A date taken without a known offset counts as UTC.
    /// </summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>
    /// Gets the end of the item dates to keep, exclusive, or <c>null</c>.
    /// </summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>
    /// Gets the smallest resolution to keep, in pixels (width × height), or <c>null</c>. Items whose size isn't known
    /// yet don't match a resolution filter.
    /// </summary>
    public long? MinPixels { get; init; }

    /// <summary>
    /// Gets the largest resolution to keep, in pixels, or <c>null</c>.
    /// </summary>
    public long? MaxPixels { get; init; }

    /// <summary>
    /// Gets whether to keep only favorites (<c>true</c>), only non-favorites (<c>false</c>), or both (<c>null</c>).
    /// </summary>
    public bool? IsFavorite { get; init; }

    /// <summary>
    /// Gets the processing statuses to keep, or <c>null</c> for all but Excluded.
    /// </summary>
    public IReadOnlyCollection<MediaItemStatus>? Statuses { get; init; }

    /// <summary>
    /// Gets the review status to keep, or <c>null</c> for both.
    /// </summary>
    public ReviewStatus? ReviewStatus { get; init; }

    /// <summary>
    /// Gets the collection whose members to keep, or <c>null</c>.
    /// </summary>
    public Id<Collection>? CollectionId { get; init; }

    /// <summary>
    /// Gets user tags that an item must all have, ignoring case, or <c>null</c>.
    /// </summary>
    public IReadOnlyCollection<TagName>? UserTags { get; init; }

    /// <summary>
    /// Gets the indexed folder to search in, or <c>null</c> for every folder.
    /// </summary>
    public Id<IndexedFolder>? FolderId { get; init; }

    /// <summary>
    /// Gets the subfolder of <see cref="FolderId"/> to search in, with <c>/</c> separators, including everything inside
    /// it; <c>null</c> or empty for the whole folder.
    /// </summary>
    public string? Subfolder { get; init; }

    /// <summary>
    /// Gets the earliest import time to keep, e.g. for Recently imported (D053), or <c>null</c>.
    /// </summary>
    public DateTimeOffset? ImportedSince { get; init; }

    /// <summary>
    /// Gets the conditions of registered filters (E10-S01), or <c>null</c>. Each must hold, like the conditions above;
    /// the search service builds their SQL without knowing the filters.
    /// </summary>
    public IReadOnlyList<FilterCondition>? Conditions { get; init; }
}
