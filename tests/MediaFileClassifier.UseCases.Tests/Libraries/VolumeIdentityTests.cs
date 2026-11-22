using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Monitoring;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for the volume identity of indexed folders (E04-S04-T03): stored while Online, a drive that took the folder's
/// letter isn't the folder, and a drive that came back under another letter takes the folder with it.
/// </summary>
public sealed class VolumeIdentityTests
{
    private const string Folder = @"E:\Photos";
    private const string Moved = @"F:\Photos";
    private const string Serial = "1A2B-3C4D";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Check_WhileOnline_StoresTheVolumeId_Once()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder);
        bed.Volumes.Volumes[@"E:\"] = Serial;

        var changed = await bed.CheckAsync();
        await bed.CheckAsync();

        Assert.Empty(changed);
        Assert.Equal((Serial, 1), (folder.VolumeId, bed.Library.FolderList.Updates));
    }

    [Fact]
    public async Task Check_LetterTakenByAnotherDrive_IsOffline_AndKeepsTheStoredId()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder, Serial);
        bed.Volumes.Volumes[@"E:\"] = "9999-0000";

        var changed = await bed.CheckAsync();

        Assert.Equal(FolderStatus.Offline, Assert.Single(changed).Status);
        Assert.Equal((Folder, Serial), (folder.PlatformHandle, folder.VolumeId));
        Assert.Empty(bed.Publisher.Of<FoldersReconnected>());
    }

    [Fact]
    public async Task Check_DriveBackUnderAnotherLetter_RelinksTheFolder_AndCatchesItUp()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder, Serial, offline: true);
        bed.Files.Unavailable.Add(Folder);
        bed.Volumes.Moved[Folder] = new FolderHandle(Moved, Moved);
        bed.Files.Labels[Moved] = "Backup-2TB";

        var changed = await bed.CheckAsync();

        Assert.Same(folder, Assert.Single(changed));
        Assert.Equal((Moved, Moved, FolderStatus.Online), (folder.DisplayPath, folder.PlatformHandle, folder.Status));
        Assert.Equal(("Backup-2TB", Serial), (folder.VolumeLabel, folder.VolumeId));
        Assert.Equal(1, bed.Library.FolderList.Relinks);
        Assert.Equal([folder.Id], Assert.Single(bed.Publisher.Of<FoldersReconnected>()).FolderIds);
        var activity = Assert.Single(bed.Sender.Activities);
        Assert.Equal((ActivityEventType.FolderRelinked, folder.Id), (activity.Type, activity.FolderId));
        Assert.Equal($"{Folder} → {Moved}", activity.Subject);
    }

    [Fact]
    public async Task Check_DriveBackUnderAnotherLetter_ThatOverlapsAnotherFolder_StaysOffline()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder, Serial, offline: true);
        await bed.AddFolderAsync(@"F:\");
        bed.Files.Unavailable.Add(Folder);
        bed.Volumes.Moved[Folder] = new FolderHandle(Moved, Moved);

        await bed.CheckAsync();

        Assert.Equal((Folder, FolderStatus.Offline), (folder.PlatformHandle, folder.Status));
        Assert.Equal(0, bed.Library.FolderList.Relinks);
        Assert.Empty(bed.Sender.Activities);
    }

    [Fact]
    public async Task Check_UnreachableFolderWithoutAnId_IsNotLookedForElsewhere()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder);
        bed.Files.Unavailable.Add(Folder);
        bed.Volumes.Moved[Folder] = new FolderHandle(Moved, Moved);

        await bed.CheckAsync();

        Assert.Equal((Folder, FolderStatus.Offline), (folder.PlatformHandle, folder.Status));
        Assert.Equal(0, bed.Volumes.Searches);
    }

    [Fact]
    public async Task Check_IdThatCantBeReadNow_KeepsTheFolderOnline_AndTheStoredId()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(Folder, Serial);

        var changed = await bed.CheckAsync();

        Assert.Empty(changed);
        Assert.Equal((FolderStatus.Online, Serial), (folder.Status, folder.VolumeId));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" 1A2B-3C4D ", Serial)]
    public void ChangeVolumeId_TreatsBlankAsNone_AndTrims(string? volumeId, string? stored)
    {
        var folder = IndexedFolder.Create(default, Folder, Folder, DateTimeOffset.UnixEpoch);

        folder.ChangeVolumeId(volumeId);

        Assert.Equal(stored, folder.VolumeId);
    }

    private sealed class Bed
    {
        private Bed(FakeLibrarySession session, FakeOpenLibrary library)
        {
            Session = session;
            Library = library;
            Handler = new CheckFolderAvailabilityCommandHandler(
                session, Files, TimeProvider.System, NullLogger<CheckFolderAvailabilityCommandHandler>.Instance, publisher: Publisher, volumes: Volumes, sender: Sender);
        }

        public FakeLibrarySession Session { get; }

        public FakeOpenLibrary Library { get; }

        public FakeFolderSource Files { get; } = new();

        public FakeVolumeLocator Volumes { get; } = new();

        public FakePublisher Publisher { get; } = new();

        public ActivitySender Sender { get; } = new();

        public CheckFolderAvailabilityCommandHandler Handler { get; }

        public static async Task<Bed> CreateAsync()
        {
            var session = new FakeLibrarySession();
            var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "volumes"), "volumes");
            await session.SetCurrentAsync(library);
            return new Bed(session, library);
        }

        public async Task<IndexedFolder> AddFolderAsync(string path, string? volumeId = null, bool offline = false)
        {
            var folder = new IndexedFolder(
                Domain.Identity.Id<IndexedFolder>.New(),
                Library.Library.Id,
                path,
                path,
                new FolderRules(),
                offline ? FolderStatus.Offline : FolderStatus.Online,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                volumeId: volumeId);
            await Library.Folders.AddAsync(folder, Token);
            return folder;
        }

        public Task<IReadOnlyList<IndexedFolder>> CheckAsync() => Handler.Handle(new CheckFolderAvailabilityCommand(), Token);
    }

    private sealed class ActivitySender : FakeSender
    {
        public List<RecordActivityCommand> Activities { get; } = [];

        protected override object? Handle(object request)
        {
            Activities.Add((RecordActivityCommand)request);
            return Unit.Value;
        }
    }
}
