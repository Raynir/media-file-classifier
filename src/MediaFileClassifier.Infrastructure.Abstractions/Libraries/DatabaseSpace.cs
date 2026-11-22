namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// How much of a library database file is free space that a <c>VACUUM</c> would give back (MFC-1022).
/// </summary>
/// <param name="FileBytes">Size of the database file (page count times page size; the WAL file isn't counted).</param>
/// <param name="FreeBytes">Size of the free pages inside the file.</param>
/// <param name="AvailableDiskBytes">Free space on the drive that holds the file, or <c>null</c> when it can't be read.</param>
public sealed record DatabaseSpace(long FileBytes, long FreeBytes, long? AvailableDiskBytes = null)
{
    /// <summary>
    /// Gets the size of the data in the file, which is what a compacted file would hold.
    /// </summary>
    public long UsedBytes => FileBytes - FreeBytes;
}
