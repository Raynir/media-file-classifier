using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.StartRescan;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// A library-wide rescan and a folder import that overlap never register the same path twice (MFC-1040).
/// </summary>
public sealed class RescanImportOverlapTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;
    private string otherRoot = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IJobScheduler Scheduler => bed.Jobs.Current!;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        otherRoot = bed.Root + "-added";
        Directory.CreateDirectory(otherRoot);
        File.Copy(SampleMedia.JpegWithExif, Path.Combine(otherRoot, "photo.jpg"));
        File.Copy(SampleMedia.PngWithAlpha, Path.Combine(otherRoot, "alpha.png"));
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        Directory.Delete(otherRoot, recursive: true);
    }

    [Fact]
    public async Task FolderImportedWhileALibraryRescanIsQueued_RegistersEachFileOnce()
    {
        Scheduler.Pause();
        var rescan = await bed.Sender.Send(new StartRescanCommand(), Token);
        var added = await AddFolderAsync();
        var import = Assert.Single(await bed.Sender.Send(new ImportFoldersCommand([added.Id]), Token));

        Scheduler.Resume();
        await bed.WaitUntilIdleAsync();

        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(rescan.JobId!.Value, Token))!.State);
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(import, Token))!.State);
        await AssertEachFileOnceAsync(added);
    }

    [Fact]
    public async Task FolderImportedAfterALibraryRescanRegisteredIt_LeavesTheItemsToTheRescan()
    {
        // As if a library-wide reconcile had just registered the new folder's files and queued their fingerprints.
        Scheduler.Pause();
        var added = await AddFolderAsync();
        var registered = await bed.Get<IFolderDiscovery>().RegisterFilesAsync(bed.Library, added, ["photo.jpg", "alpha.png"], Token);
        var rescan = await Scheduler.EnqueueJobAsync(
            new JobRequest(
                RescanStages.RescanJob,
                "Rescan library",
                JobPriority.UserAction,
                [.. registered.Select(registration => new WorkItemRequest(ImportStages.Fingerprint, registration.Item!.Id))]),
            Token);
        await Scheduler.PauseJobAsync(rescan, Token);
        var import = Assert.Single(await bed.Sender.Send(new ImportFoldersCommand([added.Id]), Token));

        Scheduler.Resume();
        await WaitUntilFinishedAsync(import);

        Assert.DoesNotContain(await bed.Library.Jobs.GetWorkItemsAsync(import, Token), workItem => workItem.ItemId is not null);
        await Scheduler.ResumeJobAsync(rescan, Token);
        await bed.WaitUntilIdleAsync();
        Assert.Equal(ProcessingJobState.Completed, (await bed.Library.Jobs.GetJobAsync(rescan, Token))!.State);
        await AssertEachFileOnceAsync(added);
    }

    private async Task<IndexedFolder> AddFolderAsync()
    {
        var folder = IndexedFolder.Create(bed.Library.Library.Id, otherRoot, otherRoot, DateTimeOffset.UtcNow);
        await bed.Library.Folders.AddAsync(folder, Token);
        return folder;
    }

    private async Task AssertEachFileOnceAsync(IndexedFolder folder)
    {
        var paths = (await bed.Library.Items.GetPageAsync(0, 1000, Token))
            .Where(item => item.FolderId == folder.Id)
            .Select(item => item.Path.Value)
            .Order(StringComparer.Ordinal);
        Assert.Equal(["alpha.png", "photo.jpg"], paths);
    }

    private async Task WaitUntilFinishedAsync(Id<ProcessingJob> jobId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while ((await bed.Library.Jobs.GetJobAsync(jobId, timeout.Token))!.State is not (ProcessingJobState.Completed or ProcessingJobState.Failed or ProcessingJobState.Cancelled))
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
