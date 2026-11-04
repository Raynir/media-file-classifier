namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Where libraries can be created (E02-S01-T04). See "Creating a library" in <c>docs/library-storage.md</c>.
/// </summary>
public interface ILibraryLocations
{
    /// <summary>
    /// Gets the folder new libraries go in unless the user picks another, e.g. <c>C:\Users\Ann\Media File Classifier</c>.
    /// It may not exist yet.
    /// </summary>
    string DefaultParentDirectory { get; }

    /// <summary>
    /// Looks at a folder a library could be created in. A folder that doesn't exist yet is judged by its nearest
    /// existing parent, since creating the library creates it.
    /// </summary>
    /// <param name="parentDirectory">The folder.</param>
    /// <param name="libraryName">The new library's name, a valid folder name.</param>
    /// <returns>What was found.</returns>
    LibraryLocationInfo Inspect(string parentDirectory, string libraryName);

    /// <summary>
    /// Checks whether a library folder and its database are reachable, e.g. its drive is connected.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <returns><c>true</c> if the database exists.</returns>
    bool IsAvailable(string libraryDirectory);
}

/// <summary>
/// What <see cref="ILibraryLocations.Inspect"/> found about a folder.
/// </summary>
/// <param name="IsWritable">Whether the app can create files there.</param>
/// <param name="FreeBytes">Free space on its drive, or <c>null</c> if unknown.</param>
/// <param name="IsInsideLibrary">Whether the folder is a library folder or inside one.</param>
/// <param name="LibraryExists">Whether the new library's <c>&lt;name&gt;.mfc</c> folder already exists and isn't empty.</param>
public sealed record LibraryLocationInfo(bool IsWritable, long? FreeBytes, bool IsInsideLibrary, bool LibraryExists);
