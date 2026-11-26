using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Activity.GetActivityPage;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;

namespace MediaFileClassifier.Infrastructure.Tests.Activity;

/// <summary>
/// The activity page the log window reads (E15-S04-T02), with a real library: which events can link to their items.
/// </summary>
public sealed class GetActivityPageFlowTests : IAsyncLifetime
{
    private readonly TemporaryFolder temp = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync(root: temp.Combine("child"));
        bed.Get<ActivityJobRecorder>().Start();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var import = new ActivityFilter([ActivityEventType.Import]);
        for (var i = 0; i < 750 && await bed.Library.Activity.CountAsync(import, Token) == 0; i++)
        {
            await Task.Delay(20, Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task Page_LinksAnEvent_ToItsFolder_AndAnItemEventToTheItemsFolder()
    {
        var item = (await bed.ItemsAsync())[0].Id;
        await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.Rescan, Counts: new Dictionary<string, long> { [ActivityCounts.Added] = 1 }, FolderId: bed.Folder.Id), Token);
        await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.Export, ItemId: item), Token);

        var page = await bed.Sender.Send(new GetActivityPageQuery(ActivityFilter.All, 0, 50), Token);

        Assert.Equal(
            [bed.Folder.Id, bed.Folder.Id],
            page.Entries.Where(entry => entry.Event.Type is ActivityEventType.Rescan or ActivityEventType.Export).Select(entry => entry.ShowFolderId!.Value));
    }

    [Fact]
    public async Task Page_HasNoLink_ForAFolderOrItemThatIsGone_OrAnEventWithoutOne()
    {
        await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.FolderAdded, FolderId: Id<IndexedFolder>.New()), Token);
        await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.Export, FolderId: bed.Folder.Id, ItemId: Id<MediaItem>.New()), Token);
        await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.Maintenance), Token);

        var page = await bed.Sender.Send(new GetActivityPageQuery(new ActivityFilter([ActivityEventType.FolderAdded, ActivityEventType.Export, ActivityEventType.Maintenance]), 0, 50), Token);

        Assert.Equal(3, page.Entries.Count);
        Assert.All(page.Entries, entry => Assert.Null(entry.ShowFolderId));
    }

    [Fact]
    public async Task Page_DropsTheLink_OnceTheFolderIsRemoved()
    {
        await bed.Sender.Send(new RemoveFolderCommand(bed.Folder.Id), Token);
        await bed.WaitUntilIdleAsync();

        var page = await bed.Sender.Send(new GetActivityPageQuery(new ActivityFilter([ActivityEventType.Import]), 0, 50), Token);

        var import = Assert.Single(page.Entries);
        Assert.Null(import.ShowFolderId);
    }

    [Fact]
    public async Task Page_PagesThroughTheLog_AndCountsAllMatches()
    {
        for (var i = 0; i < 5; i++)
        {
            await bed.Sender.Send(new RecordActivityCommand(ActivityEventType.Maintenance), Token);
        }

        var filter = new ActivityFilter([ActivityEventType.Maintenance]);
        var first = await bed.Sender.Send(new GetActivityPageQuery(filter, 0, 2), Token);
        var last = await bed.Sender.Send(new GetActivityPageQuery(filter, 4, 2), Token);

        Assert.Equal((2, 5, 1, 5), (first.Entries.Count, first.Total, last.Entries.Count, last.Total));
    }
}
