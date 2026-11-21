using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.Files;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Library schema 16 stores each indexed folder's volume label (E14-S07-T02), and the browser reads it with the items.
/// </summary>
public sealed class VolumeLabelSchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpgradeFrom15_ExistingFolders_HaveNoLabel()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        bed.Folder.ChangeVolumeLabel("Backup-2TB");
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations[14], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(Domain.Libraries.Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
        var folder = await bed.Library.Folders.GetAsync(bed.Folder.Id, Token);
        Assert.NotNull(folder);
        Assert.Null(folder.VolumeLabel);
    }

    [Fact]
    public async Task VolumeLabel_IsStored_AndReadWithTheItemSummaries()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        bed.Folder.ChangeVolumeLabel("Backup-2TB");
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        await bed.ReopenAsync();

        Assert.Equal("Backup-2TB", (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.VolumeLabel);
        Assert.Equal("Backup-2TB", (await bed.Library.Items.GetSummariesByIdsAsync([item.Id], Token)).Single().VolumeLabel);
    }

    [Fact]
    public async Task FileSource_ReadsTheDrivesLabel_AndNoneForAShare()
    {
        var files = new FileSystemMediaFileSource();
        var temp = Path.GetTempPath();
        var expected = new DriveInfo(Path.GetPathRoot(temp)!).VolumeLabel;

        var label = await files.GetVolumeLabelAsync(new FolderHandle(temp, temp), Token);
        var share = await files.GetVolumeLabelAsync(new FolderHandle(@"\\no-such-server\photos", "photos"), Token);

        Assert.Equal(string.IsNullOrWhiteSpace(expected) ? null : expected, label);
        Assert.Null(share);
    }
}
