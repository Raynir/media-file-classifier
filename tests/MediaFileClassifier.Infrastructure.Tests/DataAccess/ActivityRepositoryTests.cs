using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// The activity log against a real library database (E15-S04-T01): library schema 17, paging, filtering and the
/// retention of the newest 5,000 events.
/// </summary>
public sealed class ActivityRepositoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_RoundTrips_WithItsCountsAndLinks()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = Id<MediaItem>.New();
        var added = ActivityEvent.Create(
            ActivityEventType.Rescan,
            ActivitySeverity.Warning,
            LibraryTestBed.Start,
            @"D:\Photos",
            new Dictionary<string, long> { [ActivityCounts.Added] = 5, [ActivityCounts.Missing] = 2 },
            bed.Folder.Id,
            item);

        await bed.Library.Activity.AddAsync(added, Token);
        await bed.ReopenAsync();

        var stored = Assert.Single(await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 0, 10, Token));
        Assert.Equal((added.Id, ActivityEventType.Rescan, ActivitySeverity.Warning, LibraryTestBed.Start), (stored.Id, stored.Type, stored.Severity, stored.OccurredAt));
        Assert.Equal((@"D:\Photos", bed.Folder.Id, item), (stored.Subject, stored.FolderId, stored.ItemId));
        Assert.Equal(new Dictionary<string, long> { [ActivityCounts.Added] = 5, [ActivityCounts.Missing] = 2 }, stored.Counts);
    }

    [Fact]
    public async Task Event_WithoutCountsOrLinks_RoundTripsEmpty()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        await bed.Library.Activity.AddAsync(ActivityEvent.Create(ActivityEventType.BulkEdit, ActivitySeverity.Info, LibraryTestBed.Start), Token);

        var stored = Assert.Single(await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 0, 10, Token));
        Assert.Equal((null, null, null, 0), (stored.Subject, stored.FolderId, stored.ItemId, stored.Counts.Count));
    }

    [Fact]
    public async Task GetPage_ListsNewestFirst_AndPagesWithoutRepeatsOrGaps()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        for (var minute = 0; minute < 25; minute++)
        {
            await AddAsync(bed, ActivityEventType.Import, minute);
        }

        var first = await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 0, 10, Token);
        var second = await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 10, 10, Token);
        var third = await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 20, 10, Token);

        var all = first.Concat(second).Concat(third).ToList();
        Assert.Equal((10, 10, 5), (first.Count, second.Count, third.Count));
        Assert.Equal(Enumerable.Range(0, 25).Reverse().Select(minute => LibraryTestBed.Start.AddMinutes(minute)), all.Select(activity => activity.OccurredAt));
    }

    [Fact]
    public async Task EventsOfTheSameInstant_KeepAStableOrderAcrossPages()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        for (var index = 0; index < 6; index++)
        {
            await AddAsync(bed, ActivityEventType.Import, minute: 0);
        }

        var pages = (await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 0, 3, Token))
            .Concat(await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 3, 3, Token))
            .ToList();

        Assert.Equal(6, pages.Select(activity => activity.Id).Distinct().Count());
    }

    [Fact]
    public async Task Filter_ByTypeAndSeverity_ListsAndCountsOnlyMatches()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await AddAsync(bed, ActivityEventType.Import, 0);
        await AddAsync(bed, ActivityEventType.Import, 1, ActivitySeverity.Error);
        await AddAsync(bed, ActivityEventType.Rescan, 2, ActivitySeverity.Warning);
        await AddAsync(bed, ActivityEventType.FolderAdded, 3);

        var imports = new ActivityFilter([ActivityEventType.Import]);
        var importsAndRescans = new ActivityFilter([ActivityEventType.Import, ActivityEventType.Rescan]);
        var problems = new ActivityFilter(MinimumSeverity: ActivitySeverity.Warning);

        Assert.Equal((4, 2, 3, 2), (
            await bed.Library.Activity.CountAsync(ActivityFilter.All, Token),
            await bed.Library.Activity.CountAsync(imports, Token),
            await bed.Library.Activity.CountAsync(importsAndRescans, Token),
            await bed.Library.Activity.CountAsync(problems, Token)));
        Assert.All(await bed.Library.Activity.GetPageAsync(imports, 0, 10, Token), activity => Assert.Equal(ActivityEventType.Import, activity.Type));
        Assert.Equal(
            [ActivityEventType.Rescan, ActivityEventType.Import],
            (await bed.Library.Activity.GetPageAsync(problems, 0, 10, Token)).Select(activity => activity.Type));
    }

    [Fact]
    public async Task Add_KeepsOnlyTheNewestEvents_DroppingTheOldestInTheSameWrite()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var extra = 3;
        for (var minute = 0; minute < ActivityRetention.MaxEntries + extra; minute++)
        {
            await AddAsync(bed, ActivityEventType.Import, minute);
        }

        var count = await bed.Library.Activity.CountAsync(ActivityFilter.All, Token);
        var oldest = (await bed.Library.Activity.GetPageAsync(ActivityFilter.All, ActivityRetention.MaxEntries - 1, 1, Token)).Single();

        Assert.Equal(ActivityRetention.MaxEntries, count);
        Assert.Equal(LibraryTestBed.Start.AddMinutes(extra), oldest.OccurredAt);
    }

    [Fact]
    public async Task Event_OutlivesTheFolderAndItemItPointsAt()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.Activity.AddAsync(
            ActivityEvent.Create(ActivityEventType.FolderAdded, ActivitySeverity.Info, LibraryTestBed.Start, folderId: bed.Folder.Id),
            Token);

        await bed.Library.Folders.DeleteAsync(bed.Folder.Id, Token);

        var stored = Assert.Single(await bed.Library.Activity.GetPageAsync(ActivityFilter.All, 0, 10, Token));
        Assert.Equal(bed.Folder.Id, stored.FolderId);
    }

    [Fact]
    public async Task UpgradeFrom16_ExistingLibraries_GetAnEmptyLog_AtTheCurrentSchema()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        await bed.Library.DisposeAsync();
        await using (var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token))
        {
            var migrations = db.Database.GetMigrations().ToList();
            // Schema 16 is the migration before the activity log's; later migrations come after it.
            var activityLog = migrations.FindIndex(migration => migration.EndsWith("_ActivityLog", StringComparison.Ordinal));
            await db.GetService<IMigrator>().MigrateAsync(migrations[activityLog - 1], cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.Equal(Library.CurrentSchemaVersion, bed.Library.Library.SchemaVersion);
        Assert.Equal(0, await bed.Library.Activity.CountAsync(ActivityFilter.All, Token));
    }

    private static Task AddAsync(LibraryTestBed bed, ActivityEventType type, int minute, ActivitySeverity severity = ActivitySeverity.Info) =>
        bed.Library.Activity.AddAsync(ActivityEvent.Create(type, severity, LibraryTestBed.Start.AddMinutes(minute)), Token);
}
