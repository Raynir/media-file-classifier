using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Tells where a folder is on Windows (E04-S05-T02, E04-S05-T03), with no file in it opened:
/// <list type="bullet">
/// <item>A UNC path, or a path on a drive Windows reports as remote (a mapped network drive), is a network share.</item>
/// <item>A local folder under a sync root registered with the Windows cloud files API (OneDrive, Dropbox, iCloud and
/// other clients built on it) is cloud-synced, named by the provider the client registered.</item>
/// <item>A folder on Google Drive for desktop's virtual drive is cloud-synced: that drive's volume label or file system
/// name says Google Drive.</item>
/// </list>
/// Older clients that use neither aren't found, and their folders count as local.
/// </summary>
/// <param name="logger">Logger.</param>
internal sealed partial class WindowsFolderLocationProbe(ILogger<WindowsFolderLocationProbe> logger) : IFolderLocationProbe
{
    private const uint DriveRemote = 4;
    private const int SyncRootInfoProvider = 2;
    private const int MaxNameLength = 255;
    private const string GoogleDrive = "Google Drive";

    /// <inheritdoc />
    public Task<FolderLocation> ProbeAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // Asking a disconnected mapped drive can take a while, so off the caller's thread.
        return Task.Run(() => Probe(folder.Id), cancellationToken);
    }

    private FolderLocation Probe(string path)
    {
        if (PathFolderLocationProbe.IsUncPath(path))
        {
            return new FolderLocation(IsNetwork: true);
        }

        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FolderLocation.Local;
        }

        if (string.IsNullOrEmpty(root))
        {
            return FolderLocation.Local;
        }

        root = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (GetDriveType(root) == DriveRemote)
        {
            return new FolderLocation(IsNetwork: true);
        }

        return new FolderLocation(IsNetwork: false, SyncRootProvider(path) ?? GoogleDriveVolume(root));
    }

    private static string? GoogleDriveVolume(string root)
    {
        Span<char> label = stackalloc char[MaxNameLength + 1];
        Span<char> fileSystem = stackalloc char[MaxNameLength + 1];
        if (!GetVolumeInformation(root, ref MemoryMarshal.GetReference(label), (uint)label.Length, 0, 0, 0, ref MemoryMarshal.GetReference(fileSystem), (uint)fileSystem.Length))
        {
            return null;
        }

        return Text(label).Contains(GoogleDrive, StringComparison.OrdinalIgnoreCase)
            || Text(fileSystem).Contains("GoogleDrive", StringComparison.OrdinalIgnoreCase)
            ? GoogleDrive
            : null;

        static string Text(ReadOnlySpan<char> buffer) => (buffer.IndexOf('\0') is var end and >= 0 ? buffer[..end] : buffer).ToString();
    }

    private string? SyncRootProvider(string path)
    {
        try
        {
            // CF_SYNC_ROOT_PROVIDER_INFO: a status, then the provider's name and version, each up to 255 characters.
            Span<byte> buffer = stackalloc byte[sizeof(uint) + (2 * (MaxNameLength + 1) * sizeof(char))];
            var result = CfGetSyncRootInfoByPath(path, SyncRootInfoProvider, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length, out _);
            if (result < 0)
            {
                // Not under a sync root (or the folder isn't there now).
                return null;
            }

            var name = MemoryMarshal.Cast<byte, char>(buffer.Slice(sizeof(uint), (MaxNameLength + 1) * sizeof(char)));
            var end = name.IndexOf('\0');
            var provider = (end < 0 ? name : name[..end]).ToString().Trim();
            return provider.Length > 0 ? provider : "Cloud";
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows without the cloud files API.
            logger.LogDebug(exception, "The cloud files API isn't available");
            return null;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string rootPath);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(
        string rootPath, ref char volumeName, uint volumeNameSize, nint serialNumber, nint maxComponentLength, nint fileSystemFlags, ref char fileSystemName, uint fileSystemNameSize);

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CfGetSyncRootInfoByPath(string filePath, int infoClass, ref byte infoBuffer, uint infoBufferLength, out uint returnedLength);
}
