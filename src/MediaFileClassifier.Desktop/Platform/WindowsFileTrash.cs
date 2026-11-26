using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Moves files to the Windows Recycle Bin (E12-S04-T08, D024), never deleting one permanently. Two guards, each enough
/// on its own:
/// <list type="number">
/// <item>Before the shell is asked, a file is skipped if its path isn't a full local path, it's on a network share or a
/// remote drive (<see cref="TrashSkipReason.NoTrash"/>), it or its drive is read-only, or it isn't there.</item>
/// <item>The shell then refuses any file it couldn't recycle and would delete permanently, e.g. on a removable drive with
/// no Recycle Bin, or too big for the bin (<see cref="ShellRecycler"/>).</item>
/// </list>
/// Files are handled one at a time on a dedicated STA thread, so each gets its own result.
/// </summary>
/// <param name="logger">Logger.</param>
internal sealed partial class WindowsFileTrash(ILogger<WindowsFileTrash> logger) : IFileTrash
{
    private const uint DriveUnknown = 0;
    private const uint DriveNoRootDir = 1;
    private const uint DriveRemote = 4;
    private const uint DriveCdRom = 5;
    private const uint FileReadOnlyVolume = 0x0008_0000;

    /// <inheritdoc />
    public Task<IReadOnlyList<TrashResult>> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var completion = new TaskCompletionSource<IReadOnlyList<TrashResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(MoveAll(paths, cancellationToken));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Recycle Bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Moves one file to the Recycle Bin, after the checks. Must run on an STA thread.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <returns>What happened.</returns>
    internal static TrashResult MoveOne(string path)
    {
        if (Check(path) is { } reason)
        {
            return TrashResult.Skipped(path, reason);
        }

        var (outcome, error) = ShellRecycler.Recycle(path);
        return outcome switch
        {
            ShellRecycleOutcome.Recycled => TrashResult.Moved(path),
            ShellRecycleOutcome.Refused => TrashResult.Skipped(path, TrashSkipReason.NoTrash),
            _ => TrashResult.Failed(path, error ?? "The shell failed."),
        };
    }

    /// <summary>
    /// Checks a path before the shell is asked: the first guard.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>Why the file must be skipped, or <see langword="null"/> if the shell may try it.</returns>
    internal static TrashSkipReason? Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return TrashSkipReason.NotAFile;
        }

        // Before anything touches the share: a network location has no Recycle Bin.
        if (PathFolderLocationProbe.IsUncPath(path))
        {
            return TrashSkipReason.NoTrash;
        }

        if (DriveRoot(path) is not { } root)
        {
            return TrashSkipReason.NotAFile;
        }

        switch (GetDriveType(root))
        {
            case DriveRemote:
                return TrashSkipReason.NoTrash;
            case DriveCdRom:
                return TrashSkipReason.ReadOnly;
            case DriveUnknown or DriveNoRootDir:
                return TrashSkipReason.NotFound;
        }

        if (GetVolumeInformation(root, 0, 0, 0, 0, out var volumeFlags, 0, 0) && (volumeFlags & FileReadOnlyVolume) != 0)
        {
            return TrashSkipReason.ReadOnly;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return TrashSkipReason.NotFound;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Can't be read; the shell reports the error for the file.
            return null;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return TrashSkipReason.NotAFile;
        }

        return (attributes & FileAttributes.ReadOnly) != 0 ? TrashSkipReason.ReadOnly : null;
    }

    // The drive's root with a trailing separator, as GetDriveType wants it; a local device path (\\?\C:\...) gives C:\.
    private static string? DriveRoot(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        if (normalized.Length < 3 || !char.IsAsciiLetter(normalized[0]) || normalized[1] != ':' || normalized[2] != '\\')
        {
            return null;
        }

        return normalized[..3];
    }

    private List<TrashResult> MoveAll(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var results = new List<TrashResult>(paths.Count);
        foreach (var path in paths)
        {
            TrashResult result;
            try
            {
                result = cancellationToken.IsCancellationRequested
                    ? TrashResult.Skipped(path, TrashSkipReason.Cancelled)
                    : MoveOne(path);
            }
            catch (Exception exception)
            {
                // One file's failure mustn't lose the results of the files already in the bin.
                result = TrashResult.Failed(path, exception.Message);
            }

            switch (result.Outcome)
            {
                case TrashOutcome.Moved:
                    LogMoved(path);
                    break;
                case TrashOutcome.Skipped:
                    LogSkipped(path, result.SkipReason);
                    break;
                default:
                    LogFailed(path, result.Error);
                    break;
            }

            results.Add(result);
        }

        return results;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Moved {Path} to the Recycle Bin")]
    private partial void LogMoved(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Left {Path} in place, not moved to the Recycle Bin: {Reason}")]
    private partial void LogSkipped(string path, TrashSkipReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't move {Path} to the Recycle Bin: {Error}")]
    private partial void LogFailed(string path, string? error);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string rootPath);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(
        string rootPath, nint volumeName, uint volumeNameSize, nint serialNumber, nint maxComponentLength, out uint fileSystemFlags, nint fileSystemName, uint fileSystemNameSize);
}
