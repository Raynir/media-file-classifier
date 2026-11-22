using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="FolderChangeBuffer"/> (E04-S01-T02, DNEW-3): events are debounced, a file is reported once its
/// size and time are stable and it opens, and renames, deletions and new folders become the right changes.
/// </summary>
public sealed class FolderChangeBufferTests
{
    private static readonly Id<IndexedFolder> FolderId = Id<IndexedFolder>.New();
    private static readonly FolderHandle Folder = new(@"E:\Photos", "Photos");

    [Fact]
    public void Copy_OfALargeVideo_IsReportedOnce_AfterItCompletes()
    {
        var bed = new Bed();

        // The copy grows the file for 10 s, raising events all the while, and keeps it locked.
        bed.Files.Lock("clip.mp4");
        for (var second = 0; second <= 10; second++)
        {
            bed.Files.Write("clip.mp4", (second + 1) * 100_000_000L, bed.Clock.GetUtcNow());
            bed.Raise(FolderWatchEventKind.Changed, "clip.mp4");
            bed.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Empty(bed.Reported);

        // Done: the copier closes the file and the events stop.
        bed.Files.Lock("clip.mp4", isLocked: false);
        bed.Clock.Advance(TimeSpan.FromSeconds(10));

        var change = Assert.Single(bed.Reported);
        Assert.Equal(new FolderChange(FolderChangeKind.Changed, "clip.mp4", null, change.Stat), change);
        Assert.Equal(1_100_000_000L, change.Stat!.Length);
        Assert.Equal(0, bed.Buffer.PendingCount);
    }

    [Fact]
    public void Copy_ThatStaysQuietButLocked_IsReportedOnceTheFileOpens()
    {
        var bed = new Bed();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Lock("a.jpg");
        bed.Raise(FolderWatchEventKind.Created, "a.jpg");

        bed.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(bed.Reported);

        bed.Files.Lock("a.jpg", isLocked: false);
        bed.Clock.Advance(FolderChangeBuffer.MaxWait);

        Assert.Single(bed.Reported);
    }

    [Fact]
    public void Events_WithinTheQuietWait_AreOneCheck()
    {
        var bed = new Bed();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "a.jpg");
        bed.Raise(FolderWatchEventKind.Changed, "a.jpg");
        bed.Raise(FolderWatchEventKind.Changed, "a.jpg");

        bed.Clock.Advance(FolderChangeBuffer.Quiet + FolderChangeBuffer.StableFor - TimeSpan.FromMilliseconds(1));
        Assert.Empty(bed.Reported);

        bed.Clock.Advance(FolderChangeBuffer.Tick);
        Assert.Single(bed.Reported);
        Assert.Equal(1, bed.Files.Opens);
    }

    [Fact]
    public void EmptyFile_IsNotReported_UntilItIsFilled()
    {
        var bed = new Bed();
        bed.Files.Write("download.mp4", 0, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "download.mp4");
        bed.Settle();
        Assert.Empty(bed.Reported);
        Assert.Equal(0, bed.Buffer.PendingCount);

        bed.Files.Write("download.mp4", 500, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Changed, "download.mp4");
        bed.Settle();

        Assert.Equal("download.mp4", Assert.Single(bed.Reported).RelativePath);
    }

    [Fact]
    public void Deletion_IsReported_AndASafeSaveIsAChange()
    {
        var bed = new Bed();
        bed.Raise(FolderWatchEventKind.Deleted, "gone.jpg");

        // A safe save deletes the file and writes it again under the same name.
        bed.Files.Write("saved.jpg", 20, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Deleted, "saved.jpg");
        bed.Raise(FolderWatchEventKind.Created, "saved.jpg");
        bed.Settle();

        Assert.Equal(
            [new FolderChange(FolderChangeKind.Deleted, "gone.jpg"), new FolderChange(FolderChangeKind.Changed, "saved.jpg")],
            bed.Reported.Select(change => change with { Stat = null }).OrderBy(change => change.RelativePath));
    }

    [Fact]
    public void Rename_IsReportedWithTheOldPath_AndTwoRenamesAreOne()
    {
        var bed = new Bed();
        bed.Files.Write("c.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "b.jpg", "a.jpg");
        bed.Raise(FolderWatchEventKind.Renamed, "c.jpg", "b.jpg");
        bed.Settle();

        var change = Assert.Single(bed.Reported);
        Assert.Equal((FolderChangeKind.Renamed, "c.jpg", "a.jpg"), (change.Kind, change.RelativePath, change.OldRelativePath));
    }

    [Fact]
    public void Rename_OfAFileThatIsGoneMeanwhile_IsADeletionOfTheOldPath()
    {
        var bed = new Bed();
        bed.Raise(FolderWatchEventKind.Renamed, "b.jpg", "a.jpg");
        bed.Settle();

        Assert.Equal([new FolderChange(FolderChangeKind.Deleted, "a.jpg")], bed.Reported);
    }

    [Fact]
    public void Folder_MovedIn_HasItsFilesCheckedLikeNewFiles()
    {
        var bed = new Bed();
        bed.Files.Write("trip/a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("trip/day 2/b.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("other.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "trip", isDirectory: true);
        bed.Settle();

        Assert.Equal(["trip/a.jpg", "trip/day 2/b.jpg"], bed.Reported.Select(change => change.RelativePath).Order(StringComparer.Ordinal));
        Assert.All(bed.Reported, change => Assert.Equal(FolderChangeKind.Changed, change.Kind));
    }

    [Fact]
    public void Folder_Renamed_IsARenameOfEachFile()
    {
        var bed = new Bed();
        bed.Files.Write("2025/a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("2025/raw/b.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "2025", "2024", isDirectory: true);
        bed.Settle();

        Assert.Equal(
            [("2025/a.jpg", "2024/a.jpg"), ("2025/raw/b.jpg", "2024/raw/b.jpg")],
            bed.Reported.Select(change => (change.RelativePath, change.OldRelativePath!)).OrderBy(change => change.RelativePath, StringComparer.Ordinal));
    }

    [Fact]
    public void NewFile_RenamedBeforeItSettles_IsAChangeAtItsNewPath()
    {
        var bed = new Bed();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "a.jpg");
        bed.Files.Delete("a.jpg");
        bed.Files.Write("b.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "b.jpg", "a.jpg");
        bed.Settle();

        // The library never saw a.jpg, so its deletion is nothing to it.
        Assert.Equal(
            [(FolderChangeKind.Changed, "b.jpg", null), (FolderChangeKind.Deleted, "a.jpg", null)],
            bed.Reported.Select(change => (change.Kind, change.RelativePath, change.OldRelativePath)).OrderBy(change => change.Kind));
    }

    [Fact]
    public void SafeSave_ByARenameOverADeletedFile_IsAChange_NotARename()
    {
        var bed = new Bed();
        bed.Raise(FolderWatchEventKind.Deleted, "p.jpg");
        bed.Files.Write("p.jpg", 20, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "p.jpg", "p-new.jpg");
        bed.Settle();

        Assert.Equal(
            [(FolderChangeKind.Changed, "p.jpg"), (FolderChangeKind.Deleted, "p-new.jpg")],
            bed.Reported.Select(change => (change.Kind, change.RelativePath)).OrderBy(change => change.Kind));
        Assert.All(bed.Reported, change => Assert.Null(change.OldRelativePath));
    }

    [Fact]
    public void NewFolder_RenamedBeforeItIsListed_IsStillANewFolder()
    {
        var bed = new Bed();
        bed.Raise(FolderWatchEventKind.Created, "New folder", isDirectory: true);
        bed.Files.Write("Trip/a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "Trip", "New folder", isDirectory: true);
        bed.Settle();

        var change = Assert.Single(bed.Reported);
        Assert.Equal((FolderChangeKind.Changed, "Trip/a.jpg", null), (change.Kind, change.RelativePath, change.OldRelativePath));
    }

    [Fact]
    public void Folder_RenamedWhileACopyIntoItSettles_ReportsTheCopyAtItsNewPath_Once()
    {
        var bed = new Bed();
        bed.Files.Write("A/old.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("A/big.mp4", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "A/big.mp4");
        bed.Files.Delete("A/old.jpg");
        bed.Files.Delete("A/big.mp4");
        bed.Files.Write("B/old.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("B/big.mp4", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Renamed, "B", "A", isDirectory: true);
        bed.Settle();

        Assert.Equal(
            [(FolderChangeKind.Changed, "B/big.mp4", null), (FolderChangeKind.Deleted, "A/big.mp4", null), (FolderChangeKind.Renamed, "B/old.jpg", "A/old.jpg")],
            bed.Reported.Select(change => (change.Kind, change.RelativePath, change.OldRelativePath)).OrderBy(change => change.Kind));
    }

    [Fact]
    public void Check_ThatFailsUnexpectedly_DropsThatPath_AndReportsTheOthers()
    {
        var bed = new Bed();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("b.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Break("a.jpg");
        bed.Raise(FolderWatchEventKind.Created, "a.jpg");
        bed.Raise(FolderWatchEventKind.Created, "b.jpg");
        bed.Settle();

        Assert.Equal(["b.jpg"], bed.Reported.Select(change => change.RelativePath));
        Assert.Equal(0, bed.Buffer.PendingCount);
    }

    [Fact]
    public void NewFolder_FilesTheFilterLeavesOut_AreNotChecked()
    {
        var bed = new Bed(includes: path => path.EndsWith(".jpg", StringComparison.Ordinal));
        bed.Files.Write("trip/a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Files.Write("trip/notes.txt", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "trip", isDirectory: true);
        bed.Settle();

        Assert.Equal(["trip/a.jpg"], bed.Reported.Select(change => change.RelativePath));
        Assert.Equal(1, bed.Files.Opens);
    }

    [Fact]
    public void Forget_DropsWhatIsWaiting()
    {
        var bed = new Bed();
        bed.Files.Write("a.jpg", 10, bed.Clock.GetUtcNow());
        bed.Raise(FolderWatchEventKind.Created, "a.jpg");

        bed.Buffer.Forget(FolderId);
        bed.Settle();

        Assert.Empty(bed.Reported);
    }

    /// <summary>
    /// A buffer on in-memory files and a manual clock, recording what it reports.
    /// </summary>
    private sealed class Bed
    {
        public Bed(Func<string, bool>? includes = null)
        {
            Buffer = new FolderChangeBuffer(
                Files,
                Clock,
                NullLogger.Instance,
                (folderId, changes, _) =>
                {
                    Assert.Equal(FolderId, folderId);
                    Reported.AddRange(changes);
                    return Task.CompletedTask;
                },
                includes is null ? null : (_, path) => includes(path));
        }

        public ManualClock Clock { get; } = new();

        public InMemoryMediaFileSource Files { get; } = new();

        public FolderChangeBuffer Buffer { get; }

        public List<FolderChange> Reported { get; } = [];

        public void Raise(FolderWatchEventKind kind, string path, string? oldPath = null, bool isDirectory = false) =>
            Buffer.Record(FolderId, Folder, new FolderWatchEvent(kind, path, oldPath, isDirectory));

        /// <summary>
        /// Lets every wait run out, as if nothing happened for a minute.
        /// </summary>
        public void Settle() => Clock.Advance(TimeSpan.FromMinutes(1));
    }
}
