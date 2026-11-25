using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.UseCases.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Watching;

/// <summary>
/// The watching flow on a real folder (E04-S01-T02): the file-system watcher feeds the change buffer, which checks the
/// real files on the system clock.
/// </summary>
public sealed class FolderWatchingFlowTests
{
    [Fact]
    public async Task Copy_OfALargeVideo_IsReportedOnce_AfterTheCopyCompletes()
    {
        var token = TestContext.Current.CancellationToken;
        using var folder = new TemporaryFolder();
        var handle = LocalFolder.ToHandle(folder.Path);
        var folderId = Id<IndexedFolder>.New();
        var reported = new ConcurrentQueue<(FolderChange Change, DateTimeOffset At)>();
        using var buffer = new FolderChangeBuffer(new FileSystemMediaFileSource(), TimeProvider.System, NullLogger.Instance, (_, changes, _) =>
        {
            foreach (var change in changes)
            {
                reported.Enqueue((change, DateTimeOffset.UtcNow));
            }

            return Task.CompletedTask;
        });
        using var watcher = new FileSystemFolderWatcherFactory().Watch(handle);
        watcher.Changed += (_, change) => buffer.Record(folderId, handle, change);

        // A slow copy: 40 MB in 1 MB chunks, pausing between them, with the file open for writing all along.
        var chunk = new byte[1 << 20];
        DateTimeOffset copied;
        await using (var copy = new FileStream(folder.Combine("clip.mp4"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            for (var index = 0; index < 40; index++)
            {
                await copy.WriteAsync(chunk, token);
                await copy.FlushAsync(token);
                await Task.Delay(50, token);
            }

            copied = DateTimeOffset.UtcNow;
        }

        var end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (reported.IsEmpty && DateTime.UtcNow < end)
        {
            await Task.Delay(100, token);
        }

        // Anything a late event would still report has had its time.
        await Task.Delay(FolderChangeBuffer.Quiet + FolderChangeBuffer.StableFor + TimeSpan.FromSeconds(1), token);

        var (change, at) = Assert.Single(reported);
        Assert.Equal((FolderChangeKind.Changed, "clip.mp4", 40L << 20), (change.Kind, change.RelativePath, change.Stat!.Length));
        Assert.True(at >= copied, "The file was reported before the copy completed.");
        Assert.Equal(0, buffer.PendingCount);
    }

    [Fact]
    public async Task RenamedFolder_ThatIsGoneWhenChecked_IsDropped()
    {
        // MFC-1032: a gone rename taken for a folder, or a folder renamed again, has nothing left to list.
        var token = TestContext.Current.CancellationToken;
        using var folder = new TemporaryFolder();
        var handle = LocalFolder.ToHandle(folder.Path);
        var reported = new ConcurrentQueue<FolderChange>();
        using var buffer = new FolderChangeBuffer(new FileSystemMediaFileSource(), TimeProvider.System, NullLogger.Instance, (_, changes, _) =>
        {
            foreach (var change in changes)
            {
                reported.Enqueue(change);
            }

            return Task.CompletedTask;
        });

        buffer.Record(Id<IndexedFolder>.New(), handle, new FolderWatchEvent(FolderWatchEventKind.Renamed, "Photos2", "Photos", IsDirectory: true));

        // It's checked again twice first, in case its next rename comes late.
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (buffer.PendingCount > 0 && DateTime.UtcNow < end)
        {
            await Task.Delay(50, token);
        }

        Assert.Equal(0, buffer.PendingCount);
        Assert.Empty(reported);
    }
}
