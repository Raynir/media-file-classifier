using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Libraries.UseThisDrive;
using MediaFileClassifier.UseCases.Monitoring;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for "Use this drive" (MFC-1051): an Offline folder whose root leads to a drive with a new identity, e.g. a
/// cloned or restored disk, takes that drive as its own and comes back Online with its items.
/// </summary>
public sealed class UseThisDriveTests
{
    private const string Folder = @"E:\Photos";
    private const string Serial = "1A2B-3C4D";
    private const string NewSerial = "5E6F-7A8B";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Find_OfflineFolderOnAnotherDrive_NamesTheDrive()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        bed.Files.Labels[Folder] = "NewDisk";

        var drive = await bed.FindAsync(folder);

        Assert.Equal(new OtherDrive(NewSerial, (true, "NewDisk")), drive);
    }

    [Fact]
    public async Task Use_OfflineFolderOnAnotherDrive_StoresTheDrive_BringsItOnline_AndCatchesItUp()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        bed.Files.Labels[Folder] = "NewDisk";

        var outcome = await bed.UseAsync(folder);

        Assert.Equal(UseThisDriveOutcome.Done, outcome);
        folder = bed.Library.FolderList.Stored(folder.Id);
        Assert.Equal((FolderStatus.Online, NewSerial, "NewDisk", Folder), (folder.Status, folder.VolumeId, folder.VolumeLabel, folder.PlatformHandle));
        Assert.Equal(1, bed.Library.FolderList.Relinks);
        Assert.Equal([folder.Id], Assert.Single(bed.Publisher.Of<FoldersReconnected>()).FolderIds);
        var activity = Assert.Single(bed.Sender.Activities);
        Assert.Equal((ActivityEventType.FolderDriveReplaced, folder.Id, Folder), (activity.Type, activity.FolderId, activity.Subject));
    }

    [Fact]
    public async Task Use_OtherFoldersFromTheSameOldDrive_TakeTheDriveToo()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        var videos = await bed.AddFolderAsync(path: @"E:\Videos");
        var elsewhere = await bed.AddFolderAsync(path: @"G:\Music");
        var otherOldDrive = await bed.AddFolderAsync(path: @"E:\Docs", volumeId: "9999-0000");
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        bed.Volumes.Volumes[@"G:\"] = "7777-0000";

        var drive = await bed.FindAsync(folder);
        var result = await bed.Use.Handle(new UseThisDriveCommand(folder.Id), Token);

        Assert.Equal(1, drive!.OtherFolders);
        Assert.Equal(new UseThisDriveResult(UseThisDriveOutcome.Done, 1), result);
        videos = bed.Library.FolderList.Stored(videos.Id);
        elsewhere = bed.Library.FolderList.Stored(elsewhere.Id);
        otherOldDrive = bed.Library.FolderList.Stored(otherOldDrive.Id);
        Assert.Equal((NewSerial, FolderStatus.Online), (videos.VolumeId, videos.Status));
        Assert.Equal((Serial, FolderStatus.Offline), (elsewhere.VolumeId, elsewhere.Status));
        Assert.Equal(("9999-0000", FolderStatus.Offline), (otherOldDrive.VolumeId, otherOldDrive.Status));
        Assert.Equal([folder.Id, videos.Id], Assert.Single(bed.Publisher.Of<FoldersReconnected>()).FolderIds);
        Assert.Equal(2, bed.Sender.Activities.Count);
    }

    [Fact]
    public async Task Use_FolderRelinkedMeanwhile_IsNotFound_AndAnnouncesNothing()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        bed.Library.FolderList.RefusedRelinks.Add(folder.Id);

        var outcome = await bed.UseAsync(folder);

        Assert.Equal(UseThisDriveOutcome.NotFound, outcome);
        Assert.Empty(bed.Publisher.Of<FoldersReconnected>());
        Assert.Empty(bed.Sender.Activities);
    }

    [Fact]
    public async Task Use_ThenCheck_KeepsTheFolderOnline()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        await bed.UseAsync(folder);

        var check = new CheckFolderAvailabilityCommandHandler(
            bed.Session, bed.Files, TimeProvider.System, NullLogger<CheckFolderAvailabilityCommandHandler>.Instance, volumes: bed.Volumes);
        var changed = await check.Handle(new CheckFolderAvailabilityCommand(), Token);

        Assert.Empty(changed);
        Assert.Equal(FolderStatus.Online, bed.Library.FolderList.Stored(folder.Id).Status);
    }

    [Theory]
    [InlineData("same drive")]
    [InlineData("unreachable")]
    [InlineData("unreadable id")]
    [InlineData("online")]
    [InlineData("no stored id")]
    public async Task Use_WhenTheRootDoesntLeadToAnotherDrive_ChangesNothing(string situation)
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync(
            volumeId: situation == "no stored id" ? null : Serial,
            offline: situation != "online");
        bed.Volumes.Volumes[@"E:\"] = situation == "same drive" ? Serial : NewSerial;
        if (situation == "unreachable")
        {
            bed.Files.Unavailable.Add(Folder);
        }
        else if (situation == "unreadable id")
        {
            bed.Volumes.Volumes.Remove(@"E:\");
        }

        var drive = await bed.FindAsync(folder);
        var outcome = await bed.UseAsync(folder);

        Assert.Null(drive);
        Assert.Equal(UseThisDriveOutcome.NoOtherDrive, outcome);
        Assert.Equal(0, bed.Library.FolderList.Updates);
        Assert.Empty(bed.Publisher.Of<FoldersReconnected>());
        Assert.Empty(bed.Sender.Activities);
    }

    [Fact]
    public async Task Use_WithoutVolumeIdentities_ChangesNothing()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        var handler = new UseThisDriveCommandHandler(bed.Session, bed.Files, TimeProvider.System, NullLogger<UseThisDriveCommandHandler>.Instance);

        var outcome = await handler.Handle(new UseThisDriveCommand(folder.Id), Token);

        Assert.Equal((UseThisDriveOutcome.NoOtherDrive, FolderStatus.Offline), (outcome.Outcome, folder.Status));
    }

    [Fact]
    public async Task Use_RemovedFolder_IsNotFound()
    {
        var bed = await Bed.CreateAsync();

        var outcome = await bed.Use.Handle(new UseThisDriveCommand(Id<IndexedFolder>.New()), Token);

        Assert.Equal(UseThisDriveOutcome.NotFound, outcome.Outcome);
    }

    [Fact]
    public async Task Use_LabelThatCantBeRead_KeepsTheStoredOne()
    {
        var bed = await Bed.CreateAsync();
        var folder = await bed.AddFolderAsync();
        bed.Volumes.Volumes[@"E:\"] = NewSerial;
        bed.Files.UnreadableLabels.Add(Folder);

        var drive = await bed.FindAsync(folder);
        await bed.UseAsync(folder);

        Assert.Equal((false, (string?)null), drive!.Label);
        folder = bed.Library.FolderList.Stored(folder.Id);
        Assert.Equal(("Backup-2TB", NewSerial), (folder.VolumeLabel, folder.VolumeId));
    }

    private sealed class Bed
    {
        private Bed(FakeLibrarySession session, FakeOpenLibrary library)
        {
            Session = session;
            Library = library;
            Use = new UseThisDriveCommandHandler(
                session, Files, TimeProvider.System, NullLogger<UseThisDriveCommandHandler>.Instance, Volumes, publisher: Publisher, sender: Sender);
            Find = new FindOtherDriveQueryHandler(session, Files, NullLogger<FindOtherDriveQueryHandler>.Instance, Volumes);
        }

        public FakeLibrarySession Session { get; }

        public FakeOpenLibrary Library { get; }

        public FakeFolderSource Files { get; } = new();

        public FakeVolumeLocator Volumes { get; } = new();

        public FakePublisher Publisher { get; } = new();

        public ActivitySender Sender { get; } = new();

        public UseThisDriveCommandHandler Use { get; }

        public FindOtherDriveQueryHandler Find { get; }

        public static async Task<Bed> CreateAsync()
        {
            var session = new FakeLibrarySession();
            var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "use-this-drive"), "use-this-drive");
            await session.SetCurrentAsync(library);
            return new Bed(session, library);
        }

        public async Task<IndexedFolder> AddFolderAsync(string? volumeId = Serial, bool offline = true, string path = Folder)
        {
            var folder = new IndexedFolder(
                Id<IndexedFolder>.New(),
                Library.Library.Id,
                path,
                path,
                new FolderRules(),
                offline ? FolderStatus.Offline : FolderStatus.Online,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                volumeLabel: "Backup-2TB",
                volumeId: volumeId);
            await Library.Folders.AddAsync(folder, Token);
            return folder;
        }

        public Task<OtherDrive?> FindAsync(IndexedFolder folder) => Find.Handle(new FindOtherDriveQuery(folder.Id), Token);

        public async Task<UseThisDriveOutcome> UseAsync(IndexedFolder folder) => (await Use.Handle(new UseThisDriveCommand(folder.Id), Token)).Outcome;
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
