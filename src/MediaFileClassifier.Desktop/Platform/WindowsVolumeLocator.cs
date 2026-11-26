using System.Globalization;
using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Identifies volumes on Windows by their file system's serial number (E04-S04-T03), e.g. <c>1A2B-3C4D</c> as
/// <c>vol</c> shows it. The serial travels with the drive to another PC and survives a relabel; it changes when the drive
/// is formatted. A folder is found on another drive letter only when exactly one other mounted local volume has the
/// serial and the folder exists on it, so a cloned drive with the same serial is never guessed at. Mapped network drives
/// have no identity here, as shares opened by their UNC path don't (MFC-1051).
/// </summary>
internal sealed partial class WindowsVolumeLocator : IVolumeLocator
{
    private const uint FailCriticalErrors = 0x0001;

    /// <inheritdoc />
    public Task<string?> GetVolumeIdAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // A mapped network drive has no identity of its own, like a share opened by its UNC path: the serial is the
        // server's, which a failover or a new server changes while the folder is still there (MFC-1051).
        return Task.Run(() => LocalRoot(folder.Id) is { } root && new DriveInfo(root).DriveType != DriveType.Network ? ReadSerial(root) : null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FolderHandle?> FindOnVolumeAsync(FolderHandle folder, string volumeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeId);

        return Task.Run(() => Find(folder, volumeId), cancellationToken);
    }

    private static FolderHandle? Find(FolderHandle folder, string volumeId)
    {
        if (LocalRoot(folder.Id) is not { } oldRoot)
        {
            return null;
        }

        var matches = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is not (DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown))

            // An empty card reader or DVD drive has no volume to read, and asking it could show "No disk" (MFC-1048).
            .Where(IsReady)
            .Where(drive => !string.Equals(drive.Name, oldRoot, StringComparison.OrdinalIgnoreCase))
            .Where(drive => string.Equals(ReadSerial(drive.Name), volumeId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            return null;
        }

        var newRoot = matches[0].Name;
        var path = Path.Combine(newRoot, folder.Id[oldRoot.Length..]);
        if (!Directory.Exists(path))
        {
            return null;
        }

        // The shown path follows the same move when it starts with the old root, as it does for a picked folder.
        var display = folder.DisplayName.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(newRoot, folder.DisplayName[oldRoot.Length..])
            : path;
        return new FolderHandle(path, display);
    }

    /// <summary>
    /// Gets a path's drive root, e.g. <c>E:\</c>, or <see langword="null"/> for a share or a relative path.
    /// </summary>
    private static string? LocalRoot(string path)
    {
        string? root;
        try
        {
            root = Path.GetPathRoot(path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return root is { Length: 3 } && root[1] == ':' && root[2] == Path.DirectorySeparatorChar ? root : null;
    }

    private static bool IsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ReadSerial(string root)
    {
        // Windows would show "There is no disk in the drive" for an empty removable drive instead of failing the call
        // (MFC-1048); with critical errors off for this thread, the call just fails.
        var changedMode = SetThreadErrorMode(FailCriticalErrors, out var oldMode);
        try
        {
            if (!GetVolumeInformation(root, 0, 0, out var serial, out _, out _, 0, 0))
            {
                return null;
            }

            return string.Create(CultureInfo.InvariantCulture, $"{serial >> 16:X4}-{serial & 0xFFFF:X4}");
        }
        finally
        {
            if (changedMode)
            {
                SetThreadErrorMode(oldMode, out _);
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadErrorMode(uint newMode, out uint oldMode);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(
        string rootPath, nint volumeName, uint volumeNameSize, out uint serialNumber, out uint maxComponentLength, out uint fileSystemFlags, nint fileSystemName, uint fileSystemNameSize);
}
