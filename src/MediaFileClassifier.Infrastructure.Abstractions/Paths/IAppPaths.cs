namespace MediaFileClassifier.Infrastructure.Abstractions.Paths;

/// <summary>
/// Per-user locations the app writes to (D061). <see cref="Platform.IPlatformServices"/> exposes them to the other layers.
/// </summary>
public interface IAppPaths
{
    /// <summary>
    /// Root of the app's per-user data.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Folder for data the app can rebuild, such as thumbnails. Safe to delete.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Folder with the rolling log files.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Full path of the app settings file.
    /// </summary>
    string SettingsFile { get; }
}
