namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// App-level settings, stored as JSON in the app-data folder. Per-library settings live in the library database (E01-S03).
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Schema version written by this build. Version 2 moved the AI endpoint into <see cref="Values"/>.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Schema version of the stored file, for future migrations.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Last main window placement, or <c>null</c> to use the default. Used on a monitor setup that
    /// <see cref="MainWindowPlacements"/> doesn't know.
    /// </summary>
    public WindowPlacement? MainWindow { get; init; }

    /// <summary>
    /// Main window placements for the last few monitor setups, most recent first, or <c>null</c> if none.
    /// </summary>
    public IReadOnlyList<MonitorPlacement>? MainWindowPlacements { get; init; }

    /// <summary>
    /// Main window panel layout, or <c>null</c> to use the default.
    /// </summary>
    public MainWindowLayout? MainWindowLayout { get; init; }

    /// <summary>
    /// Folder of the library opened last, or <c>null</c> if none.
    /// </summary>
    public string? LastLibraryPath { get; init; }

    /// <summary>
    /// Values of the registered app-scope settings (E17-S01). Read and write them through <see cref="ISettingsStore"/>.
    /// </summary>
    /// <remarks>
    /// The JSON source generator sets a property missing from the file to <c>null</c>, so the setter maps that to empty.
    /// </remarks>
    public SettingValues Values
    {
        get;
        init => field = value ?? SettingValues.Empty;
    }

    = SettingValues.Empty;
}
