using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// Saves and restores the main window layout through the app settings (E13-S01-T03). The panel layout is the
/// same on every monitor setup; the window placement is kept for each of the last
/// <see cref="MaxMonitorSetups"/> monitor setups. Changes are saved <see cref="SaveDelay"/> after the last one,
/// and at once by <see cref="Flush"/> when the window closes, so a crash loses at most that last second.
/// A save that fails, e.g. while another program locks the settings file, is logged and retried with the next one.
/// </summary>
public sealed class LayoutStore : IDisposable
{
    /// <summary>
    /// How many monitor setups keep their own window placement.
    /// </summary>
    public const int MaxMonitorSetups = 5;

    /// <summary>
    /// How long after the last change the layout is saved.
    /// </summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly ISettingsService settings;
    private readonly ShellLayout layout;
    private readonly ITimer timer;
    private readonly ILogger<LayoutStore> logger;
    private readonly Lock syncRoot = new();
    private MainWindowLayout? pendingLayout;
    private MonitorPlacement? pendingPlacement;

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutStore"/> class, and restores the saved panel layout.
    /// </summary>
    /// <param name="settings">App settings.</param>
    /// <param name="layout">The panel layout to restore and watch.</param>
    /// <param name="time">Clock for the save delay.</param>
    /// <param name="logger">Logs failed saves.</param>
    public LayoutStore(ISettingsService settings, ShellLayout layout, TimeProvider time, ILogger<LayoutStore> logger)
    {
        this.settings = settings;
        this.layout = layout;
        this.logger = logger;
        timer = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        // A hand-edited file can leave a panel out, and JSON fills the missing parameter with null.
        if (settings.Current.MainWindowLayout is { Navigation: not null, Details: not null } saved)
        {
            layout.Apply(saved);
        }

        layout.Changed += OnLayoutChanged;
    }

    /// <summary>
    /// Gets the window placement saved for a monitor setup, or else the last placement on any setup.
    /// </summary>
    /// <param name="monitorSetup">Fingerprint of the connected monitors, from <see cref="MonitorSetup.Fingerprint"/>.</param>
    /// <returns>The placement, or <c>null</c> to use the default.</returns>
    public WindowPlacement? PlacementFor(string monitorSetup)
    {
        var current = settings.Current;
        return current.MainWindowPlacements?.FirstOrDefault(saved => saved.MonitorSetup == monitorSetup)?.Placement
            ?? current.MainWindow;
    }

    /// <summary>
    /// Records the window placement on a monitor setup, to be saved after <see cref="SaveDelay"/>.
    /// </summary>
    /// <param name="monitorSetup">Fingerprint of the connected monitors.</param>
    /// <param name="placement">The window placement.</param>
    public void RememberPlacement(string monitorSetup, WindowPlacement placement)
    {
        lock (syncRoot)
        {
            pendingPlacement = new MonitorPlacement(monitorSetup, placement);
        }

        ScheduleSave();
    }

    /// <summary>
    /// Saves pending changes now.
    /// </summary>
    public void Flush()
    {
        lock (syncRoot)
        {
            if (pendingLayout is null && pendingPlacement is null)
            {
                return;
            }

            var current = settings.Current;
            var updated = current with { MainWindowLayout = pendingLayout ?? current.MainWindowLayout };
            if (pendingPlacement is { } placement)
            {
                updated = updated with
                {
                    MainWindow = placement.Placement,
                    MainWindowPlacements =
                    [
                        placement,
                        .. (current.MainWindowPlacements ?? []).Where(saved => saved.MonitorSetup != placement.MonitorSetup).Take(MaxMonitorSetups - 1),
                    ],
                };
            }

            try
            {
                settings.Save(updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the pending changes for the next save. The timer thread must not throw: it would end the app.
                logger.LogWarning(ex, "Couldn't save the main window layout, will retry with the next change");
                return;
            }

            pendingLayout = null;
            pendingPlacement = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        layout.Changed -= OnLayoutChanged;
        timer.Dispose();
    }

    // The snapshot is taken here, on the UI thread, so the timer thread never reads the live layout.
    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        lock (syncRoot)
        {
            pendingLayout = layout.ToSettings();
        }

        ScheduleSave();
    }

    private void ScheduleSave() => timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
}
