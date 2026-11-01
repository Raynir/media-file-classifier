namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The settings window's categories, in sidebar order (E17-S01). The UI shows each one with the
/// <c>SettingsCategory_&lt;Id&gt;</c> string.
/// </summary>
public static class SettingCategories
{
    /// <summary>General: startup and notifications.</summary>
    public const string General = "General";

    /// <summary>Appearance: theme and how items look.</summary>
    public const string Appearance = "Appearance";

    /// <summary>Library: settings of the open library.</summary>
    public const string Library = "Library";

    /// <summary>AI and models: providers, endpoints and models.</summary>
    public const string AiModels = "AiModels";

    /// <summary>Processing: background work and its limits.</summary>
    public const string Processing = "Processing";

    /// <summary>
    /// Gets every category in sidebar order. Shortcuts and Privacy come later (E16, E17-S06).
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [General, Appearance, Library, AiModels, Processing];
}
