using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Libraries;

/// <summary>
/// Library locations on a file system with paths (E02-S01-T04). New libraries go in
/// <c>%USERPROFILE%\Media File Classifier</c>: easy to find and back up, and outside Documents, which OneDrive
/// often syncs, a risk for an open SQLite database.
/// </summary>
public sealed class LibraryLocations : ILibraryLocations
{
    /// <summary>
    /// Name of the default folder in the user's profile.
    /// </summary>
    public const string DefaultFolderName = "Media File Classifier";

    /// <inheritdoc />
    public string DefaultParentDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        DefaultFolderName);

    /// <inheritdoc />
    public LibraryLocationInfo Inspect(string parentDirectory, string libraryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        var full = Path.GetFullPath(parentDirectory);
        var existing = NearestExisting(full);
        var library = LibraryLayout.For(full, libraryName).RootDirectory;
        return new LibraryLocationInfo(
            IsWritable: existing is not null && CanWrite(existing),
            FreeBytes: existing is not null ? FreeBytes(existing) : null,
            IsInsideLibrary: IsInsideLibrary(full),
            LibraryExists: Directory.Exists(library) && Directory.EnumerateFileSystemEntries(library).Any());
    }

    /// <inheritdoc />
    public bool IsAvailable(string libraryDirectory)
        => File.Exists(Path.Combine(libraryDirectory, LibraryLayout.DatabaseFileName));

    private static string? NearestExisting(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }
        }

        return null;
    }

    // Read-only attributes and access rules are both covered by actually creating a file.
    private static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, $".mfc-write-check-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long? FreeBytes(string directory)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsInsideLibrary(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (current.EndsWith(LibraryLayout.Extension, StringComparison.OrdinalIgnoreCase)
                || File.Exists(Path.Combine(current, LibraryLayout.DatabaseFileName)))
            {
                return true;
            }
        }

        return false;
    }
}
