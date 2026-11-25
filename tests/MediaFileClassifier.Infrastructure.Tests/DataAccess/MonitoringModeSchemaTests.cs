using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Library schema 19 stores each indexed folder's monitoring mode (E04-S05-T01).
/// </summary>
public sealed class MonitoringModeSchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upgrade_ExistingFolders_AreInAuto()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        bed.Folder.ChangeMonitoringMode(MonitoringMode.Manual);
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations[migrations.FindIndex(name => name.EndsWith("_MonitoringModes", StringComparison.Ordinal)) - 1], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
        Assert.Equal(MonitoringMode.Auto, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.MonitoringMode);
    }

    [Theory]
    [InlineData(MonitoringMode.Watch)]
    [InlineData(MonitoringMode.Poll)]
    [InlineData(MonitoringMode.Manual)]
    public async Task MonitoringMode_IsStoredWithTheFolder(MonitoringMode mode)
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        bed.Folder.ChangeMonitoringMode(mode);
        await bed.Library.Folders.UpdateAsync(bed.Folder, Token);
        await bed.ReopenAsync();

        Assert.Equal(mode, (await bed.Library.Folders.GetAsync(bed.Folder.Id, Token))!.MonitoringMode);
    }
}
