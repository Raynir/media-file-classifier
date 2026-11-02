using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Why a new library's name or location is rejected (E02-S01-T04). The UI maps each one to a resource string.
/// </summary>
public enum LibraryLocationError
{
    /// <summary>The name is empty.</summary>
    NameMissing,

    /// <summary>The name can't be a folder name.</summary>
    NameInvalid,

    /// <summary>No location was chosen, or it isn't a full path.</summary>
    LocationMissing,

    /// <summary>The location is a library folder or inside one.</summary>
    InsideLibrary,

    /// <summary>A library folder with this name already exists there.</summary>
    AlreadyExists,

    /// <summary>The app can't create files there: read-only, no permission, or a failed disk.</summary>
    NotWritable,

    /// <summary>The drive has less than <see cref="LibraryLocationRules.MinimumFreeBytes"/> free.</summary>
    NotEnoughSpace,
}

/// <summary>
/// The rules for a new library's name and location (E02-S01-T04). See "Creating a library" in
/// <c>docs/library-storage.md</c>.
/// </summary>
public static class LibraryLocationRules
{
    /// <summary>
    /// The least free space a library may be created with: 1 GB, room for the database, thumbnails and migration
    /// backups of a few thousand items.
    /// </summary>
    public const long MinimumFreeBytes = 1L << 30;

    private static readonly char[] InvalidNameChars = [.. Path.GetInvalidFileNameChars().Union(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])];

    /// <summary>
    /// Checks a new library's name and location, in the order the user fixes them.
    /// </summary>
    /// <param name="name">The library name.</param>
    /// <param name="parentDirectory">The folder the library goes in.</param>
    /// <param name="locations">Looks at the file system.</param>
    /// <returns><c>null</c> if the library can be created, or the first problem.</returns>
    public static LibraryLocationError? Check(string? name, string? parentDirectory, ILibraryLocations locations)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return LibraryLocationError.NameMissing;
        }

        // Windows drops a trailing dot or space from folder names, and "." and ".." aren't names.
        if (trimmed.IndexOfAny(InvalidNameChars) >= 0 || trimmed.EndsWith('.') || trimmed.All(c => c == '.'))
        {
            return LibraryLocationError.NameInvalid;
        }

        if (string.IsNullOrWhiteSpace(parentDirectory) || !Path.IsPathFullyQualified(parentDirectory))
        {
            return LibraryLocationError.LocationMissing;
        }

        var info = locations.Inspect(parentDirectory, trimmed);
        if (info.IsInsideLibrary)
        {
            return LibraryLocationError.InsideLibrary;
        }

        if (info.LibraryExists)
        {
            return LibraryLocationError.AlreadyExists;
        }

        if (!info.IsWritable)
        {
            return LibraryLocationError.NotWritable;
        }

        return info.FreeBytes < MinimumFreeBytes ? LibraryLocationError.NotEnoughSpace : null;
    }
}
