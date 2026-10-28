namespace MediaFileClassifier.Infrastructure.Abstractions.Paths;

/// <summary>
/// Per-user locations the app writes to (D061). The platform services of E01-S07 take this over.
/// </summary>
public interface IAppPaths
{
    /// <summary>
    /// Root of the app's per-user data.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Folder with the rolling log files.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Full path of the app settings file.
    /// </summary>
    string SettingsFile { get; }
}
