namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// App-level settings, stored as JSON in the app-data folder. Per-library settings live in the library database (E01-S03).
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Schema version written by this build.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Schema version of the stored file, for future migrations.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Last main window placement, or <c>null</c> to use the default.
    /// </summary>
    public WindowPlacement? MainWindow { get; init; }

    /// <summary>
    /// Folder of the library opened last, or <c>null</c> if none.
    /// </summary>
    public string? LastLibraryPath { get; init; }

    /// <summary>
    /// AI provider configuration.
    /// </summary>
    public ProviderSettings Providers { get; init; } = new();

    /// <summary>
    /// Background processing: pool sizes, resuming, and the quit confirmation (E05-S01).
    /// </summary>
    public ProcessingSettings Processing { get; init; } = new();
}
