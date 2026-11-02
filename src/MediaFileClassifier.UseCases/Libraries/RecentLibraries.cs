using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Keeps the recent-libraries list and the last library in the app settings (E02-S02-T02).
/// </summary>
public static class RecentLibraries
{
    /// <summary>
    /// Puts a library first in the recent list and makes it the last opened one.
    /// </summary>
    /// <param name="settings">The app settings.</param>
    /// <param name="library">The library just created or opened.</param>
    /// <param name="now">The current time.</param>
    public static void Record(ISettingsService settings, IOpenLibrary library, DateTimeOffset now)
    {
        var path = library.Layout.RootDirectory;
        settings.Update(current => current with
        {
            LastLibraryPath = path,
            RecentLibraries =
            [
                new RecentLibrary(path, library.Library.Name, now),
                .. (current.RecentLibraries ?? []).Where(entry => !SamePath(entry.Path, path)),
            ],
        });
    }

    /// <summary>
    /// Compares library paths the way the file system does: Windows paths ignore case.
    /// </summary>
    /// <param name="a">A path.</param>
    /// <param name="b">Another path.</param>
    /// <returns><c>true</c> if both name the same folder.</returns>
    public static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(a),
        Path.TrimEndingDirectorySeparator(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
