using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Files;

/// <summary>
/// Tests for <see cref="FileSystemMediaFileSource.ScanAsync"/> (E03-S02-T01).
/// </summary>
public sealed class FolderScanTests : IDisposable
{
    private readonly TemporaryFolder temp = new();
    private readonly FileSystemMediaFileSource source = new();
    private readonly FolderHandle folder;
    private readonly List<string> links = [];
    private string? lockedFolder;

    public FolderScanTests()
    {
        Directory.CreateDirectory(temp.Combine("2024", "trip"));
        Directory.CreateDirectory(temp.Combine("raw"));
        File.WriteAllText(temp.Combine("top.jpg"), "top");
        File.WriteAllText(temp.Combine("2024", "trip", "a.jpg"), "a");
        File.WriteAllText(temp.Combine("raw", "b.jpg"), "b");
        var hidden = temp.Combine("Thumbs.db");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        folder = LocalFolder.ToHandle(temp.Path);
    }

    /// <summary>
    /// Creates a junction on Windows, which needs no special rights unlike a symbolic link, and a symbolic link elsewhere.
    /// </summary>
    /// <param name="link">Path of the new link.</param>
    /// <param name="target">Folder it points to.</param>
    internal static void CreateFolderLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task ScanAsync_ListsFilesAndFolders_SkippingHiddenFiles()
    {
        var entries = await ScanAsync(new FolderScanOptions());

        Assert.Equal(["2024/trip/a.jpg", "raw/b.jpg", "top.jpg"], Paths(entries, FolderScanEntryKind.File));
        Assert.Equal(["", "2024", "2024/trip", "raw"], Paths(entries, FolderScanEntryKind.Folder));
        var top = entries.Single(entry => entry.RelativePath == "top.jpg").File!;
        Assert.Equal(3, top.Length);
        Assert.NotNull(top.CreatedUtc);
    }

    [Fact]
    public async Task ScanAsync_FileNotStoredLocally_IsReportedAsCloudPlaceholder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File attributes for cloud files exist only on Windows.");
        File.SetAttributes(temp.Combine("raw", "b.jpg"), FileAttributes.Offline);

        var entries = await ScanAsync(new FolderScanOptions());

        var files = entries.Where(entry => entry.Kind == FolderScanEntryKind.File).ToDictionary(entry => entry.RelativePath, entry => entry.File!);
        Assert.True(files["raw/b.jpg"].IsCloudPlaceholder);
        Assert.False(files["top.jpg"].IsCloudPlaceholder);
    }

    [Fact]
    public async Task ScanAsync_IncludeHidden_ListsHiddenFiles()
    {
        var entries = await ScanAsync(new FolderScanOptions { IncludeHidden = true });

        Assert.Contains("Thumbs.db", Paths(entries, FolderScanEntryKind.File));
    }

    [Fact]
    public async Task ScanAsync_WithoutSubfolders_ListsOnlyTheRoot()
    {
        var entries = await ScanAsync(new FolderScanOptions { IncludeSubfolders = false });

        Assert.Equal(["top.jpg"], Paths(entries, FolderScanEntryKind.File));
        Assert.Equal([""], Paths(entries, FolderScanEntryKind.Folder));
    }

    [Fact]
    public async Task ScanAsync_PrunesFoldersTheCheckRejects()
    {
        var entries = await ScanAsync(new FolderScanOptions { ShouldEnterFolder = path => path != "raw" });

        Assert.Equal(["2024/trip/a.jpg", "top.jpg"], Paths(entries, FolderScanEntryKind.File));
    }

    [Fact]
    public async Task ScanAsync_LinkLoop_IsSkippedAndReported()
    {
        // A link from deep inside the tree back to its root: following it would never end.
        var link = temp.Combine("2024", "trip", "loop");
        CreateFolderLink(link, temp.Path);
        links.Add(link);

        var entries = await ScanAsync(new FolderScanOptions());

        Assert.Equal(["2024/trip/a.jpg", "raw/b.jpg", "top.jpg"], Paths(entries, FolderScanEntryKind.File));
        Assert.Equal(["2024/trip/loop"], Paths(entries, FolderScanEntryKind.SkippedLink));
    }

    [Fact]
    public async Task ScanAsync_UnreadableFolder_IsReportedAndTheScanGoesOn()
    {
        Directory.CreateDirectory(temp.Combine("locked"));
        File.WriteAllText(temp.Combine("locked", "secret.jpg"), "secret");
        DenyListing(temp.Combine("locked"));

        var entries = await ScanAsync(new FolderScanOptions());

        Assert.Equal(["2024/trip/a.jpg", "raw/b.jpg", "top.jpg"], Paths(entries, FolderScanEntryKind.File));
        var unreadable = Assert.Single(entries, entry => entry.Kind == FolderScanEntryKind.UnreadableFolder);
        Assert.Equal("locked", unreadable.RelativePath);
        Assert.False(string.IsNullOrEmpty(unreadable.Error));
    }

    [Fact]
    public async Task ScanAsync_MissingFolder_Throws()
    {
        var missing = LocalFolder.ToHandle(temp.Combine("missing"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            async () => await source.ScanAsync(missing, new FolderScanOptions(), TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScanAsync_Cancelled_Stops()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.ScanAsync(folder, new FolderScanOptions(), cancellation.Token).ToListAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (lockedFolder is not null)
        {
            AllowListing(lockedFolder);
        }

        // A recursive delete of the root fails on a link to the root, so links go first, on their own.
        foreach (var link in links)
        {
            Directory.Delete(link);
        }

        temp.Dispose();
    }

    private static string[] Paths(IEnumerable<FolderScanEntry> entries, FolderScanEntryKind kind) =>
        [.. entries.Where(entry => entry.Kind == kind).Select(entry => entry.RelativePath).Order(StringComparer.Ordinal)];

    private static void AllowListing(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            security.RemoveAccessRule(ListingDenial());
            directory.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule ListingDenial() =>
        new(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);

    private void DenyListing(string path)
    {
        lockedFolder = path;
        if (OperatingSystem.IsWindows())
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            security.AddAccessRule(ListingDenial());
            directory.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }
    }

    private async Task<List<FolderScanEntry>> ScanAsync(FolderScanOptions options) =>
        await source.ScanAsync(folder, options, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
}
