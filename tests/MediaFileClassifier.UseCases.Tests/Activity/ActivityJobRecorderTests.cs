using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Activity;
using MediaFileClassifier.UseCases.Activity.RecordActivity;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Maintenance;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Activity;

/// <summary>
/// Tests for <see cref="ActivityJobRecorder"/> (E15-S04-T01): a finished import, rescan, analysis or maintenance job
/// makes one event, with how it ended.
/// </summary>
public sealed class ActivityJobRecorderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_Succeeded_RecordsAnInfoEvent_ForItsFolder()
    {
        var bed = await CreateAsync();
        var folder = bed.AddFolder();
        bed.Store(ImportStages.ImportJob, folder.Id);

        await bed.Recorder.RecordAsync(Finished(ImportStages.ImportJob, bed.JobId, items: 120, failed: 0), Token);

        var command = Assert.Single(bed.Sender.Commands);
        Assert.Equal((ActivityEventType.Import, ActivitySeverity.Info, @"D:\Photos", folder.Id), (command.Type, command.Severity, command.Subject, command.FolderId));
        Assert.Equal(120L, command.Counts![ActivityCounts.Items]);
        Assert.Equal(0L, command.Counts[ActivityCounts.Failed]);
    }

    [Theory]
    [InlineData(10, 3, ActivityEventType.Import, ImportStages.ImportJob, ActivitySeverity.Warning)]
    [InlineData(10, 10, ActivityEventType.Import, ImportStages.ImportJob, ActivitySeverity.Error)]
    [InlineData(4, 1, ActivityEventType.Analysis, AnalysisStages.AnalysisJob, ActivitySeverity.Warning)]
    [InlineData(4, 0, ActivityEventType.Analysis, AnalysisStages.AnalysisJob, ActivitySeverity.Info)]
    public async Task ImportAndAnalysis_SeverityFollowsTheFailedItems(int items, int failed, ActivityEventType type, string job, ActivitySeverity expected)
    {
        var bed = await CreateAsync();
        bed.Store(job, folderId: null);

        await bed.Recorder.RecordAsync(Finished(job, bed.JobId, items, failed), Token);

        var command = Assert.Single(bed.Sender.Commands);
        Assert.Equal((type, expected), (command.Type, command.Severity));
        Assert.Equal((long)failed, command.Counts![ActivityCounts.Failed]);
    }

    [Fact]
    public async Task Rescan_RecordsItsSummary()
    {
        var bed = await CreateAsync();
        var folder = bed.AddFolder();
        bed.Store(RescanStages.RescanJob, folder.Id, result: new RescanCounts(Folders: 2, Offline: 1, New: 5, Moved: 3, Missing: 2, Modified: 4, Unchanged: 90).ToJson());

        await bed.Recorder.RecordAsync(Finished(RescanStages.RescanJob, bed.JobId, items: 0, failed: 0), Token);

        var command = Assert.Single(bed.Sender.Commands);
        Assert.Equal(ActivityEventType.Rescan, command.Type);
        Assert.Equal(
            new Dictionary<string, long>
            {
                [ActivityCounts.Folders] = 2,
                [ActivityCounts.Offline] = 1,
                [ActivityCounts.Added] = 5,
                [ActivityCounts.Moved] = 3,
                [ActivityCounts.Missing] = 2,
                [ActivityCounts.Modified] = 4,
                [ActivityCounts.Failed] = 0,
            },
            command.Counts);
    }

    [Fact]
    public async Task Thumbnails_RecordsAMaintenanceEvent_WithoutTheEnglishJobDescription()
    {
        var bed = await CreateAsync();
        bed.Store(MaintenanceJobs.ThumbnailsJob, folderId: null);

        await bed.Recorder.RecordAsync(Finished(MaintenanceJobs.ThumbnailsJob, bed.JobId, items: 50, failed: 0), Token);

        var command = Assert.Single(bed.Sender.Commands);
        Assert.Equal((ActivityEventType.Maintenance, null), (command.Type, command.Subject));
    }

    [Fact]
    public async Task FailedJob_WithoutItems_IsAnError()
    {
        var bed = await CreateAsync();
        bed.Store(ImportStages.ImportJob, folderId: null);

        await bed.Recorder.RecordAsync(Finished(ImportStages.ImportJob, bed.JobId, items: 0, failed: 0, ProcessingJobState.Failed), Token);

        Assert.Equal(ActivitySeverity.Error, Assert.Single(bed.Sender.Commands).Severity);
    }

    [Theory]
    [InlineData(ImportStages.FullHashJob)]
    [InlineData("unknown")]
    public async Task OtherJobTypes_RecordNothing(string job)
    {
        var bed = await CreateAsync();
        bed.Store(job, folderId: null);

        await bed.Recorder.RecordAsync(Finished(job, bed.JobId, items: 9, failed: 0), Token);

        Assert.Empty(bed.Sender.Commands);
    }

    [Fact]
    public async Task ImportWithoutItems_RecordsNothing()
    {
        var bed = await CreateAsync();
        bed.Store(ImportStages.ImportJob, folderId: null);

        await bed.Recorder.RecordAsync(Finished(ImportStages.ImportJob, bed.JobId, items: 0, failed: 0), Token);

        Assert.Empty(bed.Sender.Commands);
    }

    [Fact]
    public async Task JobMissingFromTheLibrary_OrNoOpenLibrary_RecordsNothing()
    {
        var bed = await CreateAsync();

        await bed.Recorder.RecordAsync(Finished(ImportStages.ImportJob, bed.JobId, items: 5, failed: 0), Token);
        await bed.Session.SetCurrentAsync(null);
        await bed.Recorder.RecordAsync(Finished(ImportStages.ImportJob, bed.JobId, items: 5, failed: 0), Token);

        Assert.Empty(bed.Sender.Commands);
    }

    [Fact]
    public async Task Start_RecordsJobsTheJobSystemReports_AndASecondStartDoesNotDoubleThem()
    {
        var bed = await CreateAsync();
        bed.Store(ImportStages.ImportJob, folderId: null);

        bed.Recorder.Start();
        bed.Recorder.Start();
        bed.Progress.Finish(Finished(ImportStages.ImportJob, bed.JobId, items: 3, failed: 0));
        await bed.Sender.WaitForAsync(1, Token);

        Assert.Single(bed.Sender.Commands);
    }

    [Fact]
    public async Task Start_WhenRecordingThrows_TheJobSystemIsNotAffected()
    {
        var bed = await CreateAsync();
        bed.Store(ImportStages.ImportJob, folderId: null);
        bed.Sender.Throws = true;
        bed.Recorder.Start();

        var failure = Record.Exception(() => bed.Progress.Finish(Finished(ImportStages.ImportJob, bed.JobId, items: 3, failed: 0)));

        Assert.Null(failure);
    }

    private static JobFinishedEventArgs Finished(string type, Id<ProcessingJob> jobId, int items, int failed, ProcessingJobState state = ProcessingJobState.Completed) =>
        new(new JobProgress(jobId, type, "job", [new StageProgress(type + ".stage", items, items - failed, failed, IsWholeTarget: false)], null), state);

    private static async Task<Bed> CreateAsync()
    {
        var session = new FakeLibrarySession();
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "x"), "x");
        await session.SetCurrentAsync(library);
        return new Bed(session, library);
    }

    private sealed class Bed
    {
        public Bed(FakeLibrarySession session, FakeOpenLibrary library)
        {
            Session = session;
            Library = library;
            Recorder = new ActivityJobRecorder(Progress, session, Sender, NullLogger<ActivityJobRecorder>.Instance);
        }

        public FakeLibrarySession Session { get; }

        public FakeOpenLibrary Library { get; }

        public Id<ProcessingJob> JobId { get; } = Id<ProcessingJob>.New();

        public StubJobProgress Progress { get; } = new();

        public RecordingSender Sender { get; } = new();

        public ActivityJobRecorder Recorder { get; }

        public IndexedFolder AddFolder()
        {
            var folder = IndexedFolder.Create(Library.Library.Id, @"D:\Photos", @"D:\Photos", DateTimeOffset.UnixEpoch);
            Library.FolderList.Folders.Add(folder);
            return folder;
        }

        public void Store(string type, Id<IndexedFolder>? folderId, string? result = null) =>
            Library.JobList.StoredJob = new ProcessingJob
            {
                Id = JobId,
                Type = type,
                Description = "Job description",
                FolderId = folderId,
                Result = result,
                State = ProcessingJobState.Completed,
                CreatedAt = DateTimeOffset.UnixEpoch,
            };
    }

    private sealed class StubJobProgress : IJobProgress
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler<JobFinishedEventArgs>? JobFinished;

        public ProgressSummary Current => ProgressSummary.Empty;

        public void Finish(JobFinishedEventArgs finished) => JobFinished?.Invoke(this, finished);
    }

    private sealed class RecordingSender : FakeSender
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<RecordActivityCommand> commands = [];

        public IReadOnlyList<RecordActivityCommand> Commands => [.. commands];

        public bool Throws { get; set; }

        public async Task WaitForAsync(int count, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (commands.Count < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, cancellationToken);
            }
        }

        protected override object? Handle(object request)
        {
            if (Throws)
            {
                throw new InvalidOperationException("no");
            }

            commands.Enqueue(Assert.IsType<RecordActivityCommand>(request));
            return Unit.Value;
        }
    }
}
