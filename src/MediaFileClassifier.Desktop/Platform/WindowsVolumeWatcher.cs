using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Reports volume arrival and removal on Windows (E14-S07-T04, DNEW-4). Windows broadcasts <c>WM_DEVICECHANGE</c>
/// with <c>DBT_DEVICEARRIVAL</c> or <c>DBT_DEVICEREMOVECOMPLETE</c> and a <c>DBT_DEVTYP_VOLUME</c> header to every
/// top-level window when a drive letter appears or goes (USB drives, memory cards, mapped network drives), so this
/// hooks the main window's window procedure. Network shares opened by UNC path raise nothing; the poll of Offline
/// folders covers them.
/// </summary>
internal sealed class WindowsVolumeWatcher : IVolumeWatcher
{
    private const uint DeviceChangeMessage = 0x0219;
    private const int DeviceArrival = 0x8000;
    private const int DeviceRemoveComplete = 0x8004;
    private const int VolumeDeviceType = 2;

    // DEV_BROADCAST_HDR: dbch_size (DWORD), then dbch_devicetype (DWORD).
    private const int DeviceTypeOffset = 4;

    /// <inheritdoc />
    public event EventHandler? VolumesChanged;

    /// <summary>
    /// Hooks the main window once the app has started.
    /// </summary>
    /// <param name="lifetime">The desktop lifetime, whose main window is set by then.</param>
    public void Attach(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lifetime.Startup += (_, _) =>
        {
            if (lifetime.MainWindow is { } window)
            {
                Win32Properties.AddWndProcHookCallback(window, OnWindowMessage);
            }
        };
    }

    /// <summary>
    /// Raises <see cref="VolumesChanged"/> as a volume arrival does, for the UI smoke tool's <c>plug-folder</c> step.
    /// </summary>
    internal void SimulateVolumeChange() => VolumesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Says whether a window message reports a volume arriving or going away.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The event, for <c>WM_DEVICECHANGE</c>.</param>
    /// <param name="lParam">The <c>DEV_BROADCAST_HDR</c>, for arrival and removal.</param>
    /// <returns>Whether it's a volume change.</returns>
    private static bool IsVolumeChange(uint message, nint wParam, nint lParam) =>
        message == DeviceChangeMessage
        && (wParam == DeviceArrival || wParam == DeviceRemoveComplete)
        && lParam != 0
        && Marshal.ReadInt32(lParam, DeviceTypeOffset) == VolumeDeviceType;

    private nint OnWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (IsVolumeChange(msg, wParam, lParam))
        {
            VolumesChanged?.Invoke(this, EventArgs.Empty);
        }

        // Left unhandled, so the window and other hooks see the message too.
        return 0;
    }
}
