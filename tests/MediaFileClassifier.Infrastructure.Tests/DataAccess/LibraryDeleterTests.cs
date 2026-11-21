using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using MediaFileClassifier.Testing.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryDeleter"/> (E02-S07-T01): only the library's own files go; unknown files and media
/// files survive, inside the library folder and next to it.
/// </summary>
public sealed class LibraryDeleterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LibraryDeleter NewDeleter() => new(TimeProvider.System, NullLogger<LibraryDeleter>.Instance);

    [Fact]
    public async Task Delete_RemovesEveryKnownFile_AndTheEmptyFolder()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        await bed.Library.DisposeAsync();
        WriteKnownFiles(layout);

        var result = await NewDeleter().DeleteAsync(layout.RootDirectory, Token);

        Assert.True(result.FolderRemoved);
        Assert.Empty(result.KeptEntries);
        Assert.False(Directory.Exists(layout.RootDirectory));
        Assert.True(Directory.Exists(bed.Temporary.ParentDirectory));
    }

    [Fact]
    public async Task Delete_UnknownFilesInsideTheLibraryFolder_Survive_AndTheFolderStays()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        await bed.Library.DisposeAsync();
        WriteKnownFiles(layout);
        string[] unknown =
        [
            Path.Combine(layout.RootDirectory, "notes.txt"),
            Path.Combine(layout.RootDirectory, "library.db.bak"),
            Path.Combine(layout.RootDirectory, "Photos", "IMG_0001.jpg"),
            Path.Combine(layout.RootDirectory, "Photos", "clip.mp4"),
            Path.Combine(layout.ThumbnailsDirectory, "mine.webp"),
            Path.Combine(layout.ThumbnailsDirectory, "a7", "holiday.webp"),
            Path.Combine(layout.ThumbnailsDirectory, "a7", "019a3b2c-0000-7000-8000-0000000000a7-256.webp.txt"),
            Path.Combine(layout.ThumbnailsDirectory, "custom", "019a3b2c-0000-7000-8000-0000000000a7-256.webp"),
            Path.Combine(layout.BackupsDirectory, "my-backup.db"),
            Path.Combine(layout.BackupsDirectory, "library-v3.db"),
        ];
        foreach (var file in unknown)
        {
            WriteFile(file);
        }

        var result = await NewDeleter().DeleteAsync(layout.RootDirectory, Token);

        Assert.False(result.FolderRemoved);
        Assert.All(unknown, file => Assert.True(File.Exists(file), file));
        Assert.Equal(["backups", "library.db.bak", "notes.txt", "Photos", "thumbnails"], result.KeptEntries);
        Assert.False(File.Exists(layout.DatabasePath));
        Assert.False(File.Exists(layout.LockFilePath));
        Assert.False(File.Exists(ThumbnailOf(layout, 0xa7)));
        Assert.False(Directory.Exists(Path.Combine(layout.ThumbnailsDirectory, "0f")));
        Assert.Empty(Directory.EnumerateFiles(layout.BackupsDirectory, "library-v1-*"));
    }

    [Fact]
    public async Task Delete_MediaFilesNextToTheLibraryFolder_Survive()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        await bed.Library.DisposeAsync();
        var parent = bed.Temporary.ParentDirectory;
        string[] media =
        [
            Path.Combine(parent, "IMG_0001.jpg"),
            Path.Combine(parent, "library.db"),
            Path.Combine(parent, "thumbnails", "019a3b2c-0000-7000-8000-0000000000a7-256.webp"),
            Path.Combine(parent, "Other.mfc", "library.db"),
        ];
        foreach (var file in media)
        {
            WriteFile(file);
        }

        var result = await NewDeleter().DeleteAsync(layout.RootDirectory, Token);

        Assert.True(result.FolderRemoved);
        Assert.All(media, file => Assert.True(File.Exists(file), file));
    }

    [Fact]
    public async Task Delete_LibraryOpenElsewhere_ThrowsInUse_AndDeletesNothing()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        var thumbnail = ThumbnailOf(layout, 0x3c);
        WriteFile(thumbnail);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => NewDeleter().DeleteAsync(layout.RootDirectory, Token));

        Assert.Equal(LibraryOpenError.InUse, exception.Error);
        Assert.True(File.Exists(layout.DatabasePath));
        Assert.True(File.Exists(thumbnail));
    }

    [Fact]
    public async Task Delete_DatabaseCantBeDeleted_ThrowsNotWritable_AndDeletesNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that's open.");
        await using var bed = await LibraryTestBed.CreateAsync();
        var layout = bed.Library.Layout;
        await bed.Library.DisposeAsync();
        var thumbnail = ThumbnailOf(layout, 0x3c);
        WriteFile(thumbnail);

        LibraryOpenException exception;
        using (new FileStream(layout.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            exception = await Assert.ThrowsAsync<LibraryOpenException>(() => NewDeleter().DeleteAsync(layout.RootDirectory, Token));
        }

        Assert.Equal(LibraryOpenError.NotWritable, exception.Error);
        Assert.True(File.Exists(layout.DatabasePath));
        Assert.True(File.Exists(thumbnail));
    }

    [Fact]
    public async Task Delete_NoDatabase_ThrowsNotFound_AndDeletesNothing()
    {
        using var temporary = TemporaryLibrary.Reserve("Gone");
        var layout = LibraryLayout.FromDirectory(temporary.LibraryDirectory);
        var thumbnail = ThumbnailOf(layout, 0x3c);
        WriteFile(thumbnail);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => NewDeleter().DeleteAsync(layout.RootDirectory, Token));

        Assert.Equal(LibraryOpenError.NotFound, exception.Error);
        Assert.True(File.Exists(thumbnail));
    }

    [Fact]
    public async Task Delete_ADatabaseThatIsntALibrary_ThrowsNotALibrary_AndDeletesNothing()
    {
        using var temporary = TemporaryLibrary.Create("Other");
        var layout = LibraryLayout.FromDirectory(temporary.LibraryDirectory);
        var thumbnail = ThumbnailOf(layout, 0x3c);
        WriteFile(thumbnail);

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => NewDeleter().DeleteAsync(layout.RootDirectory, Token));

        Assert.Equal(LibraryOpenError.NotALibrary, exception.Error);
        Assert.True(File.Exists(layout.DatabasePath));
        Assert.True(File.Exists(thumbnail));
        Assert.False(File.Exists(layout.LockFilePath));
    }

    [Fact]
    public async Task Inspect_ClosedLibrary_ReturnsItsNameAndItemCount_AndLeavesItOpenable()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.Items.AddRangeAsync([bed.NewItem(1), bed.NewItem(2), bed.NewItem(3)], Token);
        await bed.Library.DisposeAsync();

        var preview = await NewDeleter().InspectAsync(bed.Temporary.LibraryDirectory, Token);

        Assert.Equal(new LibraryDeletionPreview("test", 3), preview);
        await bed.OpenAgainAsync();
        Assert.Equal(3, await bed.Library.Items.CountAsync(Token));
    }

    [Fact]
    public async Task Inspect_LibraryOpenElsewhere_ThrowsInUse()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        var exception = await Assert.ThrowsAsync<LibraryOpenException>(() => NewDeleter().InspectAsync(bed.Temporary.LibraryDirectory, Token));

        Assert.Equal(LibraryOpenError.InUse, exception.Error);
    }

    [Theory]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-256.webp", true)]
    [InlineData("019A3B2C-0000-7000-8000-0000000000A7-512.WEBP", true)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-frame-12.webp", true)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-256.webp.0123456789abcdef0123456789abcdef.tmp", true)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-frame-0.webp.0123456789abcdef0123456789abcdef.tmp", true)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7.webp", false)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-256.jpg", false)]
    [InlineData("019a3b2c-0000-7000-8000-0000000000a7-256.webp.tmp", false)]
    [InlineData("holiday-256.webp", false)]
    [InlineData("IMG_0001.webp", false)]
    public void IsThumbnailFile_MatchesOnlyTheAppsNames(string name, bool known) =>
        Assert.Equal(known, LibraryFiles.IsThumbnailFile(name));

    [Theory]
    [InlineData("library-v3-20261030-120000-000.db", true)]
    [InlineData("library-v12-20261030-120000-000.db-wal", true)]
    [InlineData("library-damaged-20261030-120000-000.db", true)]
    [InlineData("library-damaged-20261030-120000-000.db-shm", true)]
    [InlineData("library-v3.db", false)]
    [InlineData("library.db", false)]
    [InlineData("library-v3-20261030-120000-000.db.bak", false)]
    [InlineData("my-backup.db", false)]
    public void IsBackupFile_MatchesOnlyTheAppsNames(string name, bool known) =>
        Assert.Equal(known, LibraryFiles.IsBackupFile(name));

    [Theory]
    [InlineData("a7", true)]
    [InlineData("0F", true)]
    [InlineData("a", false)]
    [InlineData("a7b", false)]
    [InlineData("zz", false)]
    public void IsThumbnailShard_MatchesTwoHexDigits(string name, bool known) =>
        Assert.Equal(known, LibraryFiles.IsThumbnailShard(name));

    private static string ThumbnailOf(LibraryLayout layout, int tail) =>
        layout.GetThumbnailPath(new(Guid.Parse($"019a3b2c-0000-7000-8000-0000000000{tail:x2}")), ThumbnailSize.Small);

    /// <summary>
    /// Writes one of each file the app puts in a library folder, as an open library and its jobs would leave them.
    /// </summary>
    private static void WriteKnownFiles(LibraryLayout layout)
    {
        var id = new Domain.Identity.Id<Domain.Media.MediaItem>(Guid.Parse("019a3b2c-0000-7000-8000-0000000000a7"));
        WriteFile(layout.GetThumbnailPath(id, ThumbnailSize.Small));
        WriteFile(layout.GetThumbnailPath(id, ThumbnailSize.Large));
        WriteFile(layout.GetKeyframeThumbnailPath(id, 2));
        WriteFile(layout.GetThumbnailPath(id, ThumbnailSize.Small) + ".0123456789abcdef0123456789abcdef.tmp");
        WriteFile(ThumbnailOf(layout, 0x0f));
        WriteFile(layout.DatabasePath + "-wal");
        WriteFile(layout.DatabasePath + "-shm");
        WriteFile(layout.DatabasePath + ".restoring");
        WriteFile(Path.Combine(layout.BackupsDirectory, "library-v1-20261030-120000-000.db"));
        WriteFile(Path.Combine(layout.BackupsDirectory, "library-damaged-20261030-120000-000.db"));
        WriteFile(Path.Combine(layout.BackupsDirectory, "library-damaged-20261030-120000-000.db-wal"));
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }
}
