using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Metadata;

/// <summary>
/// The last step of the date-taken chain (E03-S03): EXIF DateTimeOriginal, then the video's creation time, both read
/// by the extractors, then the file's modification time.
/// </summary>
internal static class DateTakenFallback
{
    /// <summary>
    /// Uses the file's modification time when the file has no date of its own. It is an exact instant, so it is stored
    /// as UTC with a zero offset.
    /// </summary>
    /// <param name="metadata">What the extractor read.</param>
    /// <param name="lastModifiedUtc">The file's last write time.</param>
    /// <returns>The metadata with a date taken and its source.</returns>
    public static TechnicalMetadata Apply(TechnicalMetadata metadata, DateTimeOffset lastModifiedUtc)
        => metadata.DateTaken is not null
            ? metadata
            : metadata with
            {
                DateTaken = ToWallClock(lastModifiedUtc),
                DateTakenOffset = TimeSpan.Zero,
                DateTakenSource = DateTakenSource.FileModified,
            };

    /// <summary>
    /// Gets an instant's UTC time as a wall-clock <see cref="DateTime"/>, the form <see cref="TechnicalMetadata.DateTaken"/>
    /// stores, with its offset kept apart.
    /// </summary>
    public static DateTime ToWallClock(DateTimeOffset instant) => DateTime.SpecifyKind(instant.UtcDateTime, DateTimeKind.Unspecified);
}
