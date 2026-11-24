using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Tells where a folder is on Windows (E04-S05-T02), with no file in it opened: a UNC path, or a path on a drive Windows
/// reports as remote (a mapped network drive), is a network share. Everything else is local.
/// </summary>
internal sealed partial class WindowsFolderLocationProbe : IFolderLocationProbe
{
    private const uint DriveRemote = 4;

    /// <inheritdoc />
    public Task<FolderLocation> ProbeAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // Asking a disconnected mapped drive can take a while, so off the caller's thread.
        return Task.Run(() => Probe(folder.Id), cancellationToken);
    }

    private static FolderLocation Probe(string path)
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
        return new FolderLocation(IsNetwork: GetDriveType(root) == DriveRemote);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string rootPath);
}
