using System.Globalization;
using Avalonia;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Helpers for window placement across monitor setups (E13-S01-T03).
/// </summary>
public static class MonitorSetup
{
    /// <summary>
    /// How much of the window's top edge must be on a screen for the user to grab it.
    /// </summary>
    public static readonly PixelSize MinVisibleTitleBar = new(100, 24);

    /// <summary>
    /// Describes the connected monitors, so a placement saved on one setup is restored on the same setup:
    /// each screen's bounds and scaling, in a stable order.
    /// </summary>
    /// <param name="screens">Each screen's bounds, in pixels, and scaling.</param>
    /// <returns>The fingerprint, e.g. <c>0,0,2560x1440@1.5|2560,0,1920x1080@1</c>.</returns>
    public static string Fingerprint(IEnumerable<(PixelRect Bounds, double Scaling)> screens)
        => string.Join(
            '|',
            screens
                .OrderBy(screen => screen.Bounds.X)
                .ThenBy(screen => screen.Bounds.Y)
                .Select(screen => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width}x{screen.Bounds.Height}@{screen.Scaling:0.##}")));

    /// <summary>
    /// Checks that a saved placement's top edge is on a screen, so the user can grab and move the window.
    /// After a monitor is disconnected, it may not be.
    /// </summary>
    /// <param name="placement">The saved placement.</param>
    /// <param name="scaling">Scaling of the screen the window opens on, to turn its width into pixels.</param>
    /// <param name="workingAreas">Each screen's working area, in pixels.</param>
    /// <returns>Whether enough of the title bar is on a screen.</returns>
    public static bool IsReachable(WindowPlacement placement, double scaling, IEnumerable<PixelRect> workingAreas)
    {
        var titleBar = new PixelRect(placement.X, placement.Y, (int)(placement.Width * scaling), MinVisibleTitleBar.Height);
        return workingAreas.Any(area =>
        {
            var visible = area.Intersect(titleBar);
            return visible.Width >= Math.Min(MinVisibleTitleBar.Width, titleBar.Width) && visible.Height >= MinVisibleTitleBar.Height;
        });
    }
}
