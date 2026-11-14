using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Which items the media browser shows (E13-S02): the scope of the selected navigation entry. Items Excluded by
/// their folder's rules or by the user are never shown (D048, D024).
/// </summary>
public sealed record MediaItemFilter
{
    /// <summary>
    /// Gets the filter for every item of the library.
    /// </summary>
    public static MediaItemFilter All { get; } = new();

    /// <summary>
    /// Gets the folder to show, or <c>null</c> for every folder.
    /// </summary>
    public Id<IndexedFolder>? FolderId { get; init; }

    /// <summary>
    /// Gets the subfolder of <see cref="FolderId"/> to show, with <c>/</c> separators, including everything inside it;
    /// <c>null</c> or empty for the whole folder.
    /// </summary>
    public string? Subfolder { get; init; }

    /// <summary>
    /// Gets the earliest import time to show, e.g. for Recently imported (D053), or <c>null</c>.
    /// </summary>
    public DateTimeOffset? ImportedSince { get; init; }

    /// <summary>
    /// Gets the status to show, e.g. <see cref="MediaItemStatus.Missing"/> for the missing items filter (E02-S04-T04),
    /// or <c>null</c> for every status but Excluded.
    /// </summary>
    public MediaItemStatus? Status { get; init; }
}
