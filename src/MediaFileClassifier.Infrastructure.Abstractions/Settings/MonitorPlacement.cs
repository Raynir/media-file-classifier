namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A main window placement saved for one monitor setup (E13-S01-T03).
/// </summary>
/// <param name="MonitorSetup">Fingerprint of the connected monitors: their bounds and scaling.</param>
/// <param name="Placement">The window placement last used with that setup.</param>
public sealed record MonitorPlacement(string MonitorSetup, WindowPlacement Placement);
