namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Position and size of a window in its normal (not maximized) state.
/// </summary>
/// <param name="X">Left edge, in screen pixels.</param>
/// <param name="Y">Top edge, in screen pixels.</param>
/// <param name="Width">Width, in device-independent units.</param>
/// <param name="Height">Height, in device-independent units.</param>
/// <param name="IsMaximized">Whether the window was maximized.</param>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized);
