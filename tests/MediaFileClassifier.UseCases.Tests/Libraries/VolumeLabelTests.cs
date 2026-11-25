using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for recording each indexed folder's volume label (E14-S07-T02): read when the folder is added and again on
/// every availability check while it's Online, and kept while it's Offline or when it can't be read.
/// </summary>
public sealed class VolumeLabelTests
{
    private const string Folder = @"E:\Photos";

    [Fact]
    public async Task AddedFolder_GetsItsVolumeLabel()
    {
        var (session, library, files) = await CreateAsync();
        files.Labels[Folder] = "Backup-2TB";
        var handler = new AddLibraryFoldersCommandHandler(session, files, TimeProvider.System, NullLogger<AddLibraryFoldersCommandHandler>.Instance);

        await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(Folder, "Photos")]), TestContext.Current.CancellationToken);

        Assert.Equal("Backup-2TB", Assert.Single(library.FolderList.Folders).VolumeLabel);
    }

    [Fact]
    public async Task AvailabilityCheck_WhileOnline_StoresANewLabel_AndSkipsTheWriteWhenUnchanged()
    {
        var (session, library, files) = await CreateAsync();
        var folder = IndexedFolder.Create(library.Library.Id, Folder, Folder, DateTimeOffset.UnixEpoch);
        await library.Folders.AddAsync(folder, TestContext.Current.CancellationToken);
        var handler = Handler(session, files);

        files.Labels[Folder] = "Backup-2TB";
        var changed = await handler.Handle(new CheckFolderAvailabilityCommand(), TestContext.Current.CancellationToken);
        await handler.Handle(new CheckFolderAvailabilityCommand(), TestContext.Current.CancellationToken);

        // Only the status changes are reported; the label is saved quietly, once.
        Assert.Empty(changed);
        Assert.Equal(("Backup-2TB", 1), (library.FolderList.Stored(folder.Id).VolumeLabel, library.FolderList.Updates));
    }

    [Fact]
    public async Task AvailabilityCheck_WhileOffline_KeepsTheLastLabel()
    {
        var (session, library, files) = await CreateAsync();
        var folder = IndexedFolder.Create(library.Library.Id, Folder, Folder, DateTimeOffset.UnixEpoch);
        folder.ChangeVolumeLabel("Backup-2TB");
        await library.Folders.AddAsync(folder, TestContext.Current.CancellationToken);
        files.Unavailable.Add(Folder);

        var changed = await Handler(session, files).Handle(new CheckFolderAvailabilityCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(FolderStatus.Offline, Assert.Single(changed).Status);
        Assert.Equal("Backup-2TB", library.FolderList.Stored(folder.Id).VolumeLabel);
    }

    [Fact]
    public async Task AvailabilityCheck_WhenTheLabelCantBeRead_KeepsTheStoredLabel_ButAVolumeWithoutOneClearsIt()
    {
        var (session, library, files) = await CreateAsync();
        var folder = IndexedFolder.Create(library.Library.Id, Folder, Folder, DateTimeOffset.UnixEpoch);
        folder.ChangeVolumeLabel("Backup-2TB");
        await library.Folders.AddAsync(folder, TestContext.Current.CancellationToken);
        var handler = Handler(session, files);

        files.UnreadableLabels.Add(Folder);
        await handler.Handle(new CheckFolderAvailabilityCommand(), TestContext.Current.CancellationToken);
        Assert.Equal(("Backup-2TB", 0), (library.FolderList.Stored(folder.Id).VolumeLabel, library.FolderList.Updates));

        files.UnreadableLabels.Remove(Folder);
        await handler.Handle(new CheckFolderAvailabilityCommand(), TestContext.Current.CancellationToken);
        Assert.Equal((null, 1), (library.FolderList.Stored(folder.Id).VolumeLabel, library.FolderList.Updates));
    }

    [Fact]
    public async Task AddedFolder_WhoseLabelCantBeRead_IsAddedWithoutOne()
    {
        var (session, library, files) = await CreateAsync();
        files.UnreadableLabels.Add(Folder);
        var handler = new AddLibraryFoldersCommandHandler(session, files, TimeProvider.System, NullLogger<AddLibraryFoldersCommandHandler>.Instance);

        await handler.Handle(new AddLibraryFoldersCommand([new FolderHandle(Folder, "Photos")]), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(library.FolderList.Folders).VolumeLabel);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" Backup ", "Backup")]
    public void ChangeVolumeLabel_TreatsBlankAsNone_AndTrims(string? label, string? stored)
    {
        var folder = IndexedFolder.Create(default, Folder, Folder, DateTimeOffset.UnixEpoch);

        folder.ChangeVolumeLabel(label);

        Assert.Equal(stored, folder.VolumeLabel);
        Assert.False(folder.ChangeVolumeLabel(label));
    }

    private static CheckFolderAvailabilityCommandHandler Handler(FakeLibrarySession session, FakeFolderSource files) =>
        new(session, files, TimeProvider.System, NullLogger<CheckFolderAvailabilityCommandHandler>.Instance);

    private static async Task<(FakeLibrarySession Session, FakeOpenLibrary Library, FakeFolderSource Files)> CreateAsync()
    {
        var session = new FakeLibrarySession();
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "x"), "x");
        await session.SetCurrentAsync(library);
        return (session, library, new FakeFolderSource());
    }
}
