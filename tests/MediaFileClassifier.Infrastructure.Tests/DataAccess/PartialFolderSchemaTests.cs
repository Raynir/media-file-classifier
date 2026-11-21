using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Library schema 15 stores the folder mode (MFC-651, D012).
/// </summary>
public sealed class PartialFolderSchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpgradeFrom14_ExistingFolders_StayFullFolders()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            await db.GetService<IMigrator>().MigrateAsync(migrations[13], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(15, bed.Library.Library.SchemaVersion);
        var folder = await bed.Library.Folders.GetAsync(bed.Folder.Id, Token);
        Assert.NotNull(folder);
        Assert.False(folder.SelectedFilesOnly);
    }

    [Fact]
    public async Task PartialFolder_IsStored_AndIndexWholeFolderIsSaved()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var partial = IndexedFolder.CreateForSelectedFiles(bed.Library.Library.Id, @"C:\Downloads", @"C:\Downloads", bed.Clock.Tick());
        await bed.Library.Folders.AddAsync(partial, Token);

        await bed.ReopenAsync();
        var stored = await bed.Library.Folders.GetAsync(partial.Id, Token);
        Assert.NotNull(stored);
        Assert.True(stored.SelectedFilesOnly);

        stored.IndexWholeFolder();
        await bed.Library.Folders.UpdateAsync(stored, Token);
        await bed.ReopenAsync();
        Assert.False((await bed.Library.Folders.GetAsync(partial.Id, Token))!.SelectedFilesOnly);
    }
}
