namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The saved state of one side panel of the main window (E13-S01).
/// </summary>
/// <param name="IsVisible">Whether the panel is shown. A hidden panel keeps its collapsed state and width.</param>
/// <param name="IsCollapsed">Whether the panel is collapsed to its icon rail or strip.</param>
/// <param name="Width">Width when expanded, in device-independent units.</param>
public sealed record PanelLayout(bool IsVisible, bool IsCollapsed, double Width);
