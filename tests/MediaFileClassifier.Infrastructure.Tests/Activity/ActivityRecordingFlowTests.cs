using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
using MediaFileClassifier.UseCases.Rescan.StartRescan;

namespace MediaFileClassifier.Infrastructure.Tests.Activity;

/// <summary>
/// Each operation the activity log lists makes an event (E15-S04-T01), with a real library, import and job system.
/// </summary>
public sealed class ActivityRecordingFlowTests : IAsyncLifetime
{
    private readonly TemporaryFolder temp = new();
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Child => temp.Combine("child");

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync(root: Child);
        bed.Get<ActivityJobRecorder>().Start();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task Import_MakesOneEvent_WithTheItemCount_AndTheFolderToOpen()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();

        var import = Assert.Single(await WaitForAsync(ActivityEventType.Import, 1));

        Assert.Equal((ActivitySeverity.Info, bed.Folder.Id, bed.Folder.DisplayPath), (import.Severity, import.FolderId, import.Subject));
        Assert.Equal(2L, import.Count(ActivityCounts.Items));
        Assert.Equal(0L, import.Count(ActivityCounts.Failed));
    }

    [Fact]
    public async Task Rescan_MakesAnEvent_WithItsSummary()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        File.Delete(Path.Combine(bed.Root, "b/alpha.png"));
        bed.Copy(SampleMedia.AnimatedGif, "new/anim.gif");

        await bed.Sender.Send(new StartRescanCommand(bed.Folder.Id), Token);
        await bed.WaitUntilIdleAsync();

        var rescan = Assert.Single(await WaitForAsync(ActivityEventType.Rescan, 1));
        Assert.Equal((1L, 1L), (rescan.Count(ActivityCounts.Added), rescan.Count(ActivityCounts.Missing)));
        Assert.Equal(bed.Folder.Id, rescan.FolderId);
    }

    [Fact]
    public async Task AddFolder_MakesAFolderAddedEvent_AndItsImport()
    {
        var other = Directory.CreateDirectory(temp.Combine("other")).FullName;
        File.Copy(SampleMedia.JpegWithExif, Path.Combine(other, "a.jpg"));

        var result = await bed.Sender.Send(new AddLibraryFoldersCommand([new FolderHandle(other, "other")]), Token);
        await bed.WaitUntilIdleAsync();

        var added = Assert.Single(await WaitForAsync(ActivityEventType.FolderAdded, 1));
        Assert.Equal((other, Assert.Single(result.Added).Id), (added.Subject, added.FolderId));
        Assert.Single(await WaitForAsync(ActivityEventType.Import, 1));
    }

    [Fact]
    public async Task RemoveFolder_MakesAFolderRemovedEvent_WithTheItemsThatWentWithIt()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var path = bed.Folder.DisplayPath;

        Assert.True(await bed.Sender.Send(new RemoveFolderCommand(bed.Folder.Id), Token));
        await bed.WaitUntilIdleAsync();

        var removed = Assert.Single(await WaitForAsync(ActivityEventType.FolderRemoved, 1));
        Assert.Equal((path, 2L), (removed.Subject, removed.Count(ActivityCounts.Items)));
    }

    [Fact]
    public async Task BulkEdit_AndItsUndo_MakeEvents_ButEditingOneItemDoesNot()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var items = await bed.ItemsAsync();

        await bed.Sender.Send(new EditMetadataCommand([items[0].Id], [new MetadataEdit.AddTag("solo")]), Token);
        await bed.Sender.Send(new EditMetadataCommand([.. items.Select(item => item.Id)], [new MetadataEdit.AddTag("trip")], CaptureUndo: true), Token);
        await bed.Sender.Send(new UndoBulkEditCommand(), Token);

        var events = await WaitForAsync(ActivityEventType.BulkEdit, 2);
        Assert.Equal(2, events.Count);
        Assert.Equal((2L, 2L), (events.Sum(activity => activity.Count(ActivityCounts.Edited)), events.Sum(activity => activity.Count(ActivityCounts.Restored))));
    }

    private async Task<IReadOnlyList<ActivityEvent>> WaitForAsync(ActivityEventType type, int count)
    {
        var filter = new ActivityFilter([type]);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await bed.Library.Activity.CountAsync(filter, Token) < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Token);
        }

        return await bed.Library.Activity.GetPageAsync(filter, 0, 50, Token);
    }
}
