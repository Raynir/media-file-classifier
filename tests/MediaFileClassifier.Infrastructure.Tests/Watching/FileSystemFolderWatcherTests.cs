using System.Collections.Concurrent;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// Tests for <c>FileSystemFolderWatcher</c> (E04-S01-T01) on a real temp folder: events carry paths relative to the
/// folder, subfolders are watched, renames keep the old path, and a missing folder can't be watched.
/// </summary>
public sealed class FileSystemFolderWatcherTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Watch_ReportsChangesInSubfolders_WithRelativePaths()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(folder.Combine("2024"));
        using var watcher = new FileSystemFolderWatcherFactory().Watch(LocalFolder.ToHandle(folder.Path));
        var events = new ConcurrentQueue<FolderWatchEvent>();
        watcher.Changed += (_, change) => events.Enqueue(change);

        await File.WriteAllBytesAsync(folder.Combine("2024", "a.jpg"), [1, 2, 3], TestContext.Current.CancellationToken);
        await WaitForAsync(() => events.Any(change => change.Kind == FolderWatchEventKind.Created && change.RelativePath == "2024/a.jpg"));

        File.Move(folder.Combine("2024", "a.jpg"), folder.Combine("2024", "b.jpg"));
        await WaitForAsync(() => events.Any(change => change is { Kind: FolderWatchEventKind.Renamed, RelativePath: "2024/b.jpg", OldRelativePath: "2024/a.jpg" }));

        Directory.CreateDirectory(folder.Combine("2025"));
        await WaitForAsync(() => events.Any(change => change is { Kind: FolderWatchEventKind.Created, RelativePath: "2025", IsDirectory: true }));
    }

    [Fact]
    public void Watch_AMissingFolder_Throws()
    {
        using var folder = new TemporaryFolder();

        Assert.Throws<DirectoryNotFoundException>(() => new FileSystemFolderWatcherFactory().Watch(LocalFolder.ToHandle(folder.Combine("missing"))));
    }

    [Fact]
    public async Task Dispose_StopsTheEvents()
    {
        using var folder = new TemporaryFolder();
        var watcher = new FileSystemFolderWatcherFactory().Watch(LocalFolder.ToHandle(folder.Path));
        var raised = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref raised);

        watcher.Dispose();
        await File.WriteAllBytesAsync(folder.Combine("a.jpg"), [1], TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref raised));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < end, "The event didn't arrive.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
