using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Import;

/// <summary>
/// Tests for the import pipeline's use-case parts that need no files or database (E03-S07).
/// </summary>
public sealed class ImportUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<Exception, bool> Failures => new()
    {
        { new MediaFileException("damaged"), true },
        { new MediaFileException("busy") { IsPermanent = false }, false },
        { new InvalidDataException(), true },
        { new FormatException(), true },
        { new NotSupportedException(), true },
        { new IOException("locked"), false },
        { new UnauthorizedAccessException(), false },
        { new FileNotFoundException(), false },
        { new TimeoutException(), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsPermanent_TreatsOnlyFileProblemsAsPermanent(Exception exception, bool permanent)
    {
        Assert.Equal(permanent, ItemStage.IsPermanent(exception));
    }

    [Theory]
    [InlineData(@"D:\Photos", "2024/trip/a.jpg", @"D:\Photos\2024\trip\a.jpg")]
    [InlineData("content://com.android.externalstorage.documents/tree/primary%3ADCIM", "a.jpg", null)]
    public void GetLocalPath_IsTheFullPathOnlyForLocalFolders(string handle, string path, string? expected)
    {
        var folder = IndexedFolder.Create(Id<Library>.New(), handle, handle, Now);
        var item = MediaItem.Discover(folder.Id, RelativePath.From(path), MediaType.Image, 1, Now, Now, null, Now);

        if (OperatingSystem.IsWindows() || expected is null)
        {
            Assert.Equal(expected, ItemStage.GetLocalPath(folder, item));
        }

        Assert.Equal(new FileLocation(new FolderHandle(handle, handle), path), ItemStage.GetLocation(folder, item));
    }

    [Fact]
    public async Task AddFolders_StartsTheImportOfTheAddedFoldersOnly()
    {
        var session = new FakeLibrarySession();
        await session.SetCurrentAsync(new FakeOpenLibrary(Infrastructure.Abstractions.Libraries.LibraryLayout.For(Path.GetTempPath(), "x"), "x"));
        var sender = new RecordingSender();
        var handler = new AddLibraryFoldersCommandHandler(session, new FakeFolderSource(), TimeProvider.System, NullLogger<AddLibraryFoldersCommandHandler>.Instance, sender);

        var added = (await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(@"E:\Photos", "Photos")]), CancellationToken.None)).Added;
        await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(@"E:\Photos", "Photos")]), CancellationToken.None);

        var import = Assert.IsType<ImportFoldersCommand>(Assert.Single(sender.Sent));
        Assert.Equal([Assert.Single(added).Id], import.FolderIds);
    }

    [Fact]
    public async Task ImportFolders_WithoutBackgroundProcessing_QueuesNothing()
    {
        var handler = new ImportFoldersCommandHandler(NullLogger<ImportFoldersCommandHandler>.Instance, TimeProvider.System);

        var started = await handler.Handle(new ImportFoldersCommand([Id<IndexedFolder>.New()]), CancellationToken.None);

        Assert.Empty(started);
    }

    private sealed class RecordingSender : FakeSender
    {
        public List<object> Sent { get; } = [];

        protected override object? Handle(object request)
        {
            Sent.Add(request);
            return null;
        }
    }
}
