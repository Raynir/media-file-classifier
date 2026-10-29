using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// Maps folder handles to file-system paths, for platforms where a granted folder is a plain path (desktop).
/// </summary>
public static class LocalFolder
{
    /// <summary>
    /// Creates the handle of a local folder.
    /// </summary>
    /// <param name="path">Path of the folder.</param>
    /// <returns>A handle whose <see cref="FolderHandle.Id"/> is the full path.</returns>
    public static FolderHandle ToHandle(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var name = Path.GetFileName(fullPath);
        return new FolderHandle(fullPath, string.IsNullOrEmpty(name) ? fullPath : name);
    }

    /// <summary>
    /// Creates the location of a local file, relative to its parent folder.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The file's location.</returns>
    public static FileLocation ToLocation(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new FileLocation(ToHandle(Path.GetDirectoryName(fullPath) ?? fullPath), Path.GetFileName(fullPath));
    }

    /// <summary>
    /// Gets the full path of a location.
    /// </summary>
    /// <param name="location">File or subfolder in a local folder.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException">The relative path is rooted or leaves the folder.</exception>
    public static string GetFullPath(FileLocation location) => GetFullPath(location.Folder, location.RelativePath);

    /// <summary>
    /// Gets the full path of a file or subfolder in a local folder.
    /// </summary>
    /// <param name="folder">Local folder.</param>
    /// <param name="relativePath">Path inside the folder with <c>/</c> or <c>\</c> separators. Empty for the folder itself.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException">The relative path is rooted or leaves the folder.</exception>
    public static string GetFullPath(FolderHandle folder, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Id));
        if (string.IsNullOrEmpty(relativePath))
        {
            return root;
        }

        var nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(nativeRelativePath))
        {
            throw new ArgumentException($"'{relativePath}' must be relative to the folder.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, nativeRelativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison) && !string.Equals(fullPath, root, comparison))
        {
            throw new ArgumentException($"'{relativePath}' leaves the folder.", nameof(relativePath));
        }

        return fullPath;
    }

    /// <summary>
    /// Gets the relative path of a file inside a local folder, with <c>/</c> separators.
    /// </summary>
    /// <param name="folder">Local folder.</param>
    /// <param name="fullPath">Full path of a file inside the folder.</param>
    /// <returns>The relative path.</returns>
    public static string GetRelativePath(FolderHandle folder, string fullPath)
        => Path.GetRelativePath(folder.Id, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
