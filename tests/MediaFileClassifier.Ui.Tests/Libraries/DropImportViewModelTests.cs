using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Import.ImportFiles;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for what dropping files and folders on the main window does (E03-S06).
/// </summary>
public sealed class DropImportViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "drop-tests");

    [Fact]
    public void Resolve_SplitsFilesAndFolders_AndIgnoresRepeats()
    {
        var folder = Path.Combine(Root, "Trips");
        var file = Path.Combine(Root, "Downloads", "a.jpg");

        var (folders, files) = DropImportViewModel.Resolve(
        [
            new DroppedPath(folder + Path.DirectorySeparatorChar, IsFolder: true),
            new DroppedPath(file, IsFolder: false),
            new DroppedPath(folder, IsFolder: true),
            new DroppedPath(file.ToUpperInvariant(), IsFolder: false),
        ]);

        var handle = Assert.Single(folders);
        Assert.Equal(folder, handle.Id);
        Assert.Equal("Trips", handle.DisplayName);
        var location = Assert.Single(files);
        Assert.Equal(Path.Combine(Root, "Downloads"), location.Folder.Id);
        Assert.Equal("a.jpg", location.RelativePath);
    }

    [Fact]
    public async Task Drop_MixedItems_ImportsFilesThenAddsFolders_AndShowsOneSummary()
    {
        var calls = new List<string>();
        var notes = new Notes();
        var model = Create(
            files =>
            {
                calls.Add($"files:{files.Count}");
                return Task.FromResult(Result(imported: 2, already: 1));
            },
            folders =>
            {
                calls.Add($"folders:{folders.Count}");
                return Task.FromResult(folders.Count);
            },
            notes);

        await model.DropAsync(
        [
            new DroppedPath(Path.Combine(Root, "a.jpg"), false),
            new DroppedPath(Path.Combine(Root, "b.jpg"), false),
            new DroppedPath(Path.Combine(Root, "c.jpg"), false),
            new DroppedPath(Path.Combine(Root, "Folder"), true),
        ]);

        Assert.Equal(["files:3", "folders:1"], calls);
        var note = Assert.Single(notes.Sent);
        Assert.Equal(NotificationSeverity.Success, note.Severity);
        Assert.Equal(
            string.Join(Environment.NewLine, "2 files are being imported.", "1 file is already in the library.", "1 folder added."),
            note.Message);
    }

    [Fact]
    public async Task Drop_OnlyFiles_DoesNotOpenTheFoldersDialog()
    {
        var folderCalls = 0;
        var model = Create(_ => Task.FromResult(Result(imported: 1)),
            _ =>
            {
                folderCalls++;
                return Task.FromResult(0);
            },
            new Notes());

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "a.jpg"), false)]);

        Assert.Equal(0, folderCalls);
    }

    [Fact]
    public async Task Drop_OnlyFolders_AddsThemAndCountsOnlyThoseAdded()
    {
        var notes = new Notes();
        var model = Create(_ => throw new InvalidOperationException("no files"), _ => Task.FromResult(2), notes);

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "A"), true), new DroppedPath(Path.Combine(Root, "B"), true), new DroppedPath(Path.Combine(Root, "C"), true)]);

        Assert.Equal("2 folders added.", Assert.Single(notes.Sent).Message);
    }

    [Fact]
    public async Task Drop_OnlyUnsupportedFiles_WarnsInsteadOfCelebrating()
    {
        var notes = new Notes();
        var model = Create(_ => Task.FromResult(Result(unsupported: 2)), _ => Task.FromResult(0), notes);

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "a.txt"), false)]);

        var note = Assert.Single(notes.Sent);
        Assert.Equal(NotificationSeverity.Warning, note.Severity);
        Assert.Equal("2 files aren't a supported format.", note.Message);
    }

    [Fact]
    public async Task Drop_FoldersRefusedByTheDialog_ShowsNoSummary()
    {
        var notes = new Notes();
        var model = Create(_ => throw new InvalidOperationException("no files"), _ => Task.FromResult(0), notes);

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "A"), true)]);

        Assert.Empty(notes.Sent);
    }

    [Fact]
    public async Task Drop_WithoutALibrary_DoesNothing()
    {
        var called = false;
        var notes = new Notes();
        var model = Create(
            _ =>
            {
                called = true;
                return Task.FromResult(Result(imported: 1));
            },
            _ => Task.FromResult(1),
            notes,
            new NoLibrary());

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "a.jpg"), false)]);

        Assert.False(model.CanAccept);
        Assert.False(called);
        Assert.Empty(notes.Sent);
    }

    [Fact]
    public async Task Drop_HidesTheOverlay()
    {
        var model = Create(_ => Task.FromResult(Result(imported: 1)), _ => Task.FromResult(0), new Notes());
        model.IsDragActive = true;

        await model.DropAsync([new DroppedPath(Path.Combine(Root, "a.jpg"), false)]);

        Assert.False(model.IsDragActive);
    }

    private static ImportFilesResult Result(int imported = 0, int already = 0, int unsupported = 0)
    {
        FileLocation File(int index) => new(new FolderHandle(Root, "drop-tests"), $"f{index}");
        return new ImportFilesResult
        {
            Imported = [.. Enumerable.Range(0, imported).Select(File)],
            AlreadyInLibrary = [.. Enumerable.Range(0, already).Select(File)],
            Unsupported = [.. Enumerable.Range(0, unsupported).Select(File)],
        };
    }

    private static DropImportViewModel Create(
        Func<IReadOnlyList<FileLocation>, Task<ImportFilesResult>> files,
        Func<IReadOnlyList<FolderHandle>, Task<int>> folders,
        Notes notes,
        ILibrarySession? session = null) => new(files, folders, notes, TestLocalizer.EnUs(), session);

    private sealed class Notes : INotificationService
    {
        public List<Notification> Sent { get; } = [];

        public void Notify(Notification notification) => Sent.Add(notification);
    }

    private sealed class NoLibrary : ILibrarySession
    {
        public event EventHandler? CurrentChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public IOpenLibrary? Current => null;

        public Task SetCurrentAsync(IOpenLibrary? library) => Task.CompletedTask;
    }
}
