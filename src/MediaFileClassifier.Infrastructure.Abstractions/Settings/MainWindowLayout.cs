namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The saved panel layout of the main window (E13-S01-T03). It's the same on every monitor setup;
/// only the window placement is kept per setup.
/// </summary>
/// <param name="Navigation">The navigation panel on the left.</param>
/// <param name="Details">The preview and details panel on the right.</param>
/// <param name="IsStatusBarVisible">Whether the status bar is shown (E13-S04).</param>
public sealed record MainWindowLayout(PanelLayout Navigation, PanelLayout Details, bool IsStatusBarVisible = true);
