namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// When a changed setting takes effect (E17-S01).
/// </summary>
public enum SettingApplyMode
{
    /// <summary>
    /// At once.
    /// </summary>
    Immediate,

    /// <summary>
    /// After the app restarts.
    /// </summary>
    RestartRequired,

    /// <summary>
    /// For new work at once; items processed before keep the old result until they are re-indexed.
    /// </summary>
    ReindexRequired,
}
