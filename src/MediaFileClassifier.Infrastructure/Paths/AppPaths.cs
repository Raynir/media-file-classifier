using MediaFileClassifier.Infrastructure.Abstractions.Paths;

namespace MediaFileClassifier.Infrastructure.Paths;

/// <summary>
/// App paths under the local application data folder, e.g. <c>%LOCALAPPDATA%\MediaFileClassifier</c> (D061).
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>
    /// Name of the app's folder inside the local application data folder.
    /// </summary>
    public const string AppFolderName = "MediaFileClassifier";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPaths"/> class in the current user's local application data folder.
    /// </summary>
    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            AppFolderName))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPaths"/> class with an explicit data directory.
    /// </summary>
    /// <param name="dataDirectory">Root of the app's data.</param>
    public AppPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        LogsDirectory = Path.Combine(dataDirectory, "logs");
        SettingsFile = Path.Combine(dataDirectory, "settings.json");
    }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string SettingsFile { get; }
}
