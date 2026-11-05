using System.Runtime.InteropServices;

namespace MediaFileClassifier.Infrastructure.AI.Native;

/// <summary>
/// Loads native libraries, such as Whisper or Tesseract, from a list of folders and then from the system's default
/// search path, and reports why loading failed instead of throwing.
/// </summary>
public static class NativeLibraryLoader
{
    /// <summary>
    /// Tries to load a native library.
    /// </summary>
    /// <param name="name">File name without the platform prefix and extension, e.g. <c>whisper</c>, or a full file name.</param>
    /// <param name="searchDirectories">Folders to look in first, in order.</param>
    /// <returns>The outcome. A loaded library stays loaded for the life of the process.</returns>
    public static NativeLibraryLoadResult TryLoad(string name, IReadOnlyList<string> searchDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(searchDirectories);

        var errors = new List<string>();
        foreach (var path in searchDirectories.SelectMany(directory => GetCandidateFileNames(name).Select(file => Path.Combine(directory, file))))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return NativeLibraryLoadResult.Success(NativeLibrary.Load(path), path);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // Found but not loadable: wrong architecture, or a dependency is missing.
                errors.Add($"{path}: {ex.Message}");
            }
        }

        if (NativeLibrary.TryLoad(name, out var handle))
        {
            return NativeLibraryLoadResult.Success(handle, name);
        }

        errors.Add($"{name} isn't on the system search path.");
        return NativeLibraryLoadResult.Failure(string.Join(" ", errors));
    }

    /// <summary>
    /// Gets the file names a library can have on this platform.
    /// </summary>
    /// <param name="name">Library name.</param>
    /// <returns>Candidate file names, the name itself first.</returns>
    internal static IEnumerable<string> GetCandidateFileNames(string name)
    {
        yield return name;
        if (Path.HasExtension(name))
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return name + ".dll";
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            yield return "lib" + name + ".dylib";
        }
        else
        {
            yield return "lib" + name + ".so";
        }
    }
}
