using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Library schema 18 stores each indexed folder's volume identity (E04-S04-T03), and a folder that followed its drive to
/// another letter is stored without undoing a newer relink or its rules.
/// </summary>
public sealed class VolumeIdSchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upgrade_ExistingFolders_HaveNoVolumeId()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        bed.Folder.ChangeVolumeId("1A2B-3C4D");
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
        Assert.Null((await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.VolumeId);
    }

    [Fact]
    public async Task VolumeId_IsStoredWithTheAvailability()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        bed.Folder.ChangeVolumeId("1A2B-3C4D");
        await bed.Library.Folders.UpdateAvailabilityAsync(bed.Folder, Token);
        await bed.ReopenAsync();

        Assert.Equal("1A2B-3C4D", (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.VolumeId);
    }

    [Fact]
    public async Task Availability_FromACopyWithoutAVolumeId_KeepsTheStoredOne()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var stale = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        bed.Folder.ChangeVolumeId("1A2B-3C4D");
        await bed.Library.Folders.UpdateAvailabilityAsync(bed.Folder, Token);

        stale.MarkOffline(DateTimeOffset.UnixEpoch.AddDays(1));
        await bed.Library.Folders.UpdateAvailabilityAsync(stale, Token);
        await bed.ReopenAsync();

        var folder = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        Assert.Equal((FolderStatus.Offline, "1A2B-3C4D"), (folder.Status, folder.VolumeId));
    }

    [Fact]
    public async Task Relink_StoresTheNewRoot_KeepsTheRules_AndRaisesTheEvents()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var oldHandle = bed.Folder.PlatformHandle;
        bed.Folder.MarkOffline(DateTimeOffset.UnixEpoch);
        await bed.Library.Folders.UpdateAvailabilityAsync(bed.Folder, Token);
        var rules = new FolderRules { IncludeHidden = true };
        var stored = await bed.Library.Folders.GetAsync(bed.Folder.Id, Token);
        stored!.ChangeRules(rules);
        await bed.Library.Folders.UpdateAsync(stored, Token);
        var events = new List<string>();
        bed.Library.Folders.StatusChanged += (_, _) => events.Add("status");
        bed.Library.Folders.Changed += (_, _) => events.Add("changed");

        bed.Folder.Relink(@"F:\Photos", @"F:\Photos", DateTimeOffset.UnixEpoch.AddDays(1));
        bed.Folder.ChangeVolumeId("1A2B-3C4D");
        var relinked = await bed.Library.Folders.RelinkAsync(bed.Folder, oldHandle, Token);
        await bed.ReopenAsync();

        var folder = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        Assert.True(relinked);
        Assert.Equal((@"F:\Photos", @"F:\Photos", FolderStatus.Online, "1A2B-3C4D"), (folder.DisplayPath, folder.PlatformHandle, folder.Status, folder.VolumeId));
        Assert.True(folder.Rules.IncludeHidden);
        Assert.Equal(["status", "changed"], events);
    }

    [Fact]
    public async Task Relink_OfAFolderRelinkedMeanwhile_StoresNothing()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var oldHandle = bed.Folder.PlatformHandle;
        var other = (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!;
        other.Relink(@"G:\Photos", @"G:\Photos", DateTimeOffset.UnixEpoch);
        await bed.Library.Folders.UpdateAsync(other, Token);

        bed.Folder.Relink(@"F:\Photos", @"F:\Photos", DateTimeOffset.UnixEpoch);
        var relinked = await bed.Library.Folders.RelinkAsync(bed.Folder, oldHandle, Token);

        Assert.False(relinked);
        Assert.Equal(@"G:\Photos", (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.PlatformHandle);
    }
}
