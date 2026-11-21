namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// How much disk space a library's own files take (E02-S05-T03), in bytes.
/// </summary>
/// <param name="DatabaseBytes">The database file with its <c>-wal</c> and <c>-shm</c> files.</param>
/// <param name="ThumbnailsBytes">Every file in the thumbnail cache, keyframe pictures included.</param>
/// <param name="BackupsBytes">Every file in the backups folder.</param>
public sealed record LibraryDiskUsage(long DatabaseBytes, long ThumbnailsBytes, long BackupsBytes)
{
    /// <summary>
    /// Gets the sum of the parts.
    /// </summary>
    public long TotalBytes => DatabaseBytes + ThumbnailsBytes + BackupsBytes;
}
