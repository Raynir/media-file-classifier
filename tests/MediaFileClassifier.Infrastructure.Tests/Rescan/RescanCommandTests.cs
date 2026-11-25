using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using MediaFileClassifier.UseCases.Rescan.StartRescan;
using MediatR;

namespace MediaFileClassifier.Infrastructure.Tests.Rescan;

/// <summary>
/// Starting a rescan and reading its summary, with a real library and job system (E02-S04-T06).
/// </summary>
public sealed class RescanCommandTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        bed.Copy(SampleMedia.WebP, "webp.webp");
        bed.Copy(SampleMedia.StaticGif, "gone.gif");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task RescanFolder_ReportsWhatChanged()
    {
        File.Delete(Path.Combine(bed.Root, "gone.gif"));
        bed.Copy(SampleMedia.AnimatedGif, "new/anim.gif");
        File.AppendAllBytes(Path.Combine(bed.Root, "webp.webp"), [1, 2, 3]);

        var started = await bed.Get<ISender>().Send(new StartRescanCommand(bed.Folder.Id), Token);
        await bed.WaitUntilIdleAsync();
        var summary = await bed.Get<ISender>().Send(new GetRescanSummaryQuery(started.JobId!.Value), Token);

        Assert.Equal(StartRescanOutcome.Started, started.Outcome);
        Assert.NotNull(summary);
        Assert.Equal(ProcessingJobState.Completed, summary.State);
        Assert.Equal(1, summary.Counts!.New);
        Assert.Equal(1, summary.Counts.Missing);
        Assert.Equal(1, summary.Counts.Modified);
        Assert.Equal(2, summary.Counts.Unchanged);
        Assert.Equal(0, summary.Counts.Moved);
        Assert.Equal(1, summary.Updated);
    }

    [Fact]
    public async Task Rescan_MeantForAnotherLibrary_StartsNothing()
    {
        // As when the library was swapped between the caller's check and the command (MFC-1042).
        await using var other = await LibraryTestBed.CreateAsync();

        var started = await bed.Get<ISender>().Send(new StartRescanCommand(Quick: true, Library: other.Library), Token);

        Assert.Equal(new StartRescanResult(null, StartRescanOutcome.Unavailable), started);
        Assert.Equal(StartRescanOutcome.Started, (await bed.Get<ISender>().Send(new StartRescanCommand(Quick: true, Library: bed.Library), Token)).Outcome);
    }

    [Fact]
    public async Task RescanLibrary_IsOneJobForEveryFolder_AndReportsAMove()
    {
        Directory.CreateDirectory(Path.Combine(bed.Root, "moved"));
        File.Move(Path.Combine(bed.Root, "b/alpha.png"), Path.Combine(bed.Root, "moved/renamed.png"));

        var started = await bed.Get<ISender>().Send(new StartRescanCommand(), Token);
        await bed.WaitUntilIdleAsync();
        var summary = await bed.Get<ISender>().Send(new GetRescanSummaryQuery(started.JobId!.Value), Token);

        var job = await bed.Library.Jobs.GetJobAsync(started.JobId!.Value, Token);
        Assert.Null(job!.FolderId);
        Assert.Equal(1, summary!.Counts!.Moved);
        Assert.Equal(0, summary.Counts.Missing);
        Assert.Equal(0, summary.Counts.New);
    }

    [Fact]
    public async Task Rescan_NothingChanged_ReportsZeroes()
    {
        var started = await bed.Get<ISender>().Send(new StartRescanCommand(), Token);
        await bed.WaitUntilIdleAsync();
        var summary = await bed.Get<ISender>().Send(new GetRescanSummaryQuery(started.JobId!.Value), Token);

        var counts = summary!.Counts!;
        Assert.Equal((0, 0, 0, 0, 4), (counts.New, counts.Moved, counts.Missing, counts.Modified, counts.Unchanged));
        Assert.Equal(0, summary.Updated);
    }

    [Fact]
    public async Task RescanFolder_NoSuchFolder_StartsNothing()
    {
        var result = await bed.Get<ISender>().Send(new StartRescanCommand(Id<MediaFileClassifier.Domain.Libraries.IndexedFolder>.New()), Token);

        Assert.Equal(StartRescanOutcome.NothingToScan, result.Outcome);
        Assert.Null(result.JobId);
    }

    [Fact]
    public async Task Summary_OfAJobThatIsStillUnfinishedOrUnknown_IsNull()
    {
        Assert.Null(await bed.Get<ISender>().Send(new GetRescanSummaryQuery(Id<ProcessingJob>.New()), Token));
    }
}
