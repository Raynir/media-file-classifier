using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Reports eject requests of the drives under watched folders on Windows (E04-S04-T01). Windows asks an app to let go
/// of a drive only for handles it registered: each folder is opened and registered for <c>DBT_DEVTYP_HANDLE</c>
/// notifications to the main window. On <c>DBT_DEVICEQUERYREMOVE</c> the folder's holders are told, and the registered
/// handle is closed too, so the eject can go ahead; on <c>DBT_DEVICEQUERYREMOVEFAILED</c> it's opened and registered
/// again and the holders are told; on <c>DBT_DEVICEREMOVECOMPLETE</c> the registration ends.
/// </summary>
/// <param name="logger">Logger.</param>
internal sealed partial class WindowsEjectNotifications(ILogger<WindowsEjectNotifications> logger) : IEjectNotifications
{
    private const uint DeviceChangeMessage = 0x0219;
    private const int DeviceQueryRemove = 0x8001;
    private const int DeviceQueryRemoveFailed = 0x8002;
    private const int DeviceRemovePending = 0x8003;
    private const int DeviceRemoveComplete = 0x8004;
    private const int HandleDeviceType = 6;
    private const int DeviceTypeOffset = 4;

    private static readonly int NotifyOffset = (int)Marshal.OffsetOf<DeviceBroadcastHandle>(nameof(DeviceBroadcastHandle.Notify));

    private readonly Lock syncRoot = new();
    private readonly Dictionary<nint, Registration> registered = [];
    private readonly List<Registration> waiting = [];
    private nint window;

    /// <summary>
    /// Hooks the main window once the app has started, and registers the folders that were waiting for it.
    /// </summary>
    /// <param name="lifetime">The desktop lifetime, whose main window is set by then.</param>
    public void Attach(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lifetime.Startup += (_, _) =>
        {
            if (lifetime.MainWindow is not { } main || main.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0)
            {
                return;
            }

            Win32Properties.AddWndProcHookCallback(main, OnWindowMessage);
            List<Registration> pending;
            lock (syncRoot)
            {
                window = handle;
                pending = [.. waiting];
                waiting.Clear();
            }

            foreach (var registration in pending)
            {
                registration.Open();
            }
        };
    }

    /// <inheritdoc />
    public IEjectRegistration? Register(FolderHandle folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // Shares and other non-local roots can't be ejected; a folder without a drive letter has nothing to register.
        var path = folder.Id;
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var registration = new Registration(this, path);
        bool open;
        lock (syncRoot)
        {
            open = window != 0;
            if (!open)
            {
                waiting.Add(registration);
            }
        }

        if (open)
        {
            registration.Open();
        }

        return registration;
    }

    private static bool IsHandleEvent(uint message, nint wParam, nint lParam) =>
        message == DeviceChangeMessage
        && wParam is DeviceQueryRemove or DeviceQueryRemoveFailed or DeviceRemovePending or DeviceRemoveComplete
        && lParam != 0
        && Marshal.ReadInt32(lParam, DeviceTypeOffset) == HandleDeviceType;

    private nint OnWindowMessage(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (!IsHandleEvent(msg, wParam, lParam))
        {
            return 0;
        }

        Registration? registration;
        lock (syncRoot)
        {
            registered.TryGetValue(Marshal.ReadIntPtr(lParam, NotifyOffset), out registration);
        }

        switch (wParam)
        {
            case DeviceQueryRemove:
                registration?.OnQueryRemove();
                break;
            case DeviceQueryRemoveFailed:
                registration?.OnQueryRemoveFailed();
                break;
            default:
                registration?.OnRemoved();
                break;
        }

        // Left unhandled: the default answer grants the removal, and other hooks see the message too.
        return 0;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("user32.dll", EntryPoint = "RegisterDeviceNotificationW", SetLastError = true)]
    private static partial nint RegisterDeviceNotification(nint recipient, ref DeviceBroadcastHandle filter, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterDeviceNotification(nint notification);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't listen for eject requests of {Folder} (error {Error}); ejecting its drive may say it's in use")]
    private partial void LogNotListening(string folder, int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Eject of the drive of {Folder} requested: the folder is released")]
    private partial void LogEjectRequested(string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "Eject of the drive of {Folder} was cancelled: the folder is watched again")]
    private partial void LogEjectCancelled(string folder);

    /// <summary>
    /// <c>DEV_BROADCAST_HANDLE</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceBroadcastHandle
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public nint Handle;
        public nint Notify;
        public Guid EventGuid;
        public int NameOffset;
        public byte Data;
    }

    /// <summary>
    /// One folder's handle and its notification. Opened while registered, closed during an eject request.
    /// </summary>
    private sealed class Registration(WindowsEjectNotifications owner, string path) : IEjectRegistration
    {
        // FILE_READ_ATTRIBUTES; read, write and delete sharing; OPEN_EXISTING; FILE_FLAG_BACKUP_SEMANTICS for folders.
        private const uint ReadAttributes = 0x80;
        private const uint ShareAll = 0x7;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;

        private SafeFileHandle? handle;
        private nint notify;
        private bool disposed;

        public event EventHandler? EjectRequested;

        public event EventHandler? EjectCancelled;

        public void Dispose()
        {
            lock (owner.syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                owner.waiting.Remove(this);
                Unregister();
                CloseHandle();
            }
        }

        public void Open()
        {
            lock (owner.syncRoot)
            {
                if (disposed || notify != 0)
                {
                    return;
                }

                var opened = CreateFile(path, ReadAttributes, ShareAll, 0, OpenExisting, BackupSemantics, 0);
                if (opened.IsInvalid)
                {
                    owner.LogNotListening(path, Marshal.GetLastPInvokeError());
                    opened.Dispose();
                    return;
                }

                var filter = new DeviceBroadcastHandle
                {
                    Size = Marshal.SizeOf<DeviceBroadcastHandle>(),
                    DeviceType = HandleDeviceType,
                    Handle = opened.DangerousGetHandle(),
                };
                var registeredNotify = RegisterDeviceNotification(owner.window, ref filter, 0);
                if (registeredNotify == 0)
                {
                    owner.LogNotListening(path, Marshal.GetLastPInvokeError());
                    opened.Dispose();
                    return;
                }

                handle = opened;
                notify = registeredNotify;
                owner.registered[notify] = this;
            }
        }

        // The notification stays registered until the eject is done or cancelled, so either is reported.
        public void OnQueryRemove()
        {
            owner.LogEjectRequested(path);
            EjectRequested?.Invoke(this, EventArgs.Empty);
            lock (owner.syncRoot)
            {
                CloseHandle();
            }
        }

        // The old notification belongs to the closed handle, so the folder is opened and registered again.
        public void OnQueryRemoveFailed()
        {
            lock (owner.syncRoot)
            {
                Unregister();
                CloseHandle();
            }

            Open();
            owner.LogEjectCancelled(path);
            EjectCancelled?.Invoke(this, EventArgs.Empty);
        }

        public void OnRemoved()
        {
            lock (owner.syncRoot)
            {
                Unregister();
                CloseHandle();
            }
        }

        private void Unregister()
        {
            if (notify == 0)
            {
                return;
            }

            owner.registered.Remove(notify);
            UnregisterDeviceNotification(notify);
            notify = 0;
        }

        private void CloseHandle()
        {
            handle?.Dispose();
            handle = null;
        }
    }
}
