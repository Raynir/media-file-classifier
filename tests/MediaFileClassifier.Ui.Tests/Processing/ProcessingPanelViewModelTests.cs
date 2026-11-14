using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.Ui.StatusBar;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Processing;
using MediaFileClassifier.UseCases.Processing.GetProcessingQueue;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// Tests for the processing panel's view model (E05-S03-T01).
/// </summary>
public sealed class ProcessingPanelViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly QueueSender sender = new();
    private readonly FakeJobProgress progress = new();
    private readonly ProgressFeed feed;
    private readonly ProcessingPanelViewModel panel;

    public ProcessingPanelViewModelTests()
    {
        feed = new ProgressFeed(progress, new ManualTimeProvider(), action => action());
        panel = new ProcessingPanelViewModel(sender, feed, Localizer.Current);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => feed.Dispose();

    [Fact]
    public async Task Refresh_GroupsJobsIntoActiveQueuedAndRecent()
    {
        var running = Job("Import photos", ProcessingJobState.Running, total: 100, done: 40);
        var queued = Job("Analyze 20 items", ProcessingJobState.Queued, total: 20, pending: 20);
        var waiting = Job("Analyze 5 items", ProcessingJobState.WaitingForModel, total: 5, pending: 5);
        var finished = Job("Rescan", ProcessingJobState.Completed, total: 10, done: 10) with { FinishedAt = Now };
        sender.Queue = new ProcessingQueue([running, queued, waiting], [finished]);

        await panel.RefreshAsync(Token);

        Assert.Equal(["Import photos"], panel.Active.Select(row => row.Description));
        Assert.Equal(["Analyze 20 items", "Analyze 5 items"], panel.Queued.Select(row => row.Description));
        Assert.Equal(["Rescan"], panel.Recent.Select(row => row.Description));
        Assert.False(panel.IsEmpty);
        Assert.Equal("Queued · 20 items", panel.Queued[0].Detail);
        Assert.Equal("Waiting for an AI model · 5 items", panel.Queued[1].Detail);
        Assert.StartsWith("Completed · ", panel.Recent[0].Detail, StringComparison.Ordinal);
        Assert.False(panel.Recent[0].ShowsProgress);
    }

    [Fact]
    public async Task Refresh_WithoutJobs_IsEmpty()
    {
        await panel.RefreshAsync(Token);

        Assert.True(panel.IsEmpty);
        Assert.Empty(panel.Active);
    }

    [Fact]
    public async Task Progress_UpdatesTheRunningRowWithoutListingTheJobsAgain()
    {
        var running = Job("Import photos", ProcessingJobState.Running, total: 340, done: 100);
        sender.Queue = new ProcessingQueue([running], []);
        await panel.RefreshAsync(Token);
        panel.Attach();
        var calls = sender.Calls;

        progress.Set(Summary(running, TimeSpan.FromMinutes(11.5), 0, new StageProgress("import.fingerprint", 340, 118, 2, false)));

        var row = Assert.Single(panel.Active);
        Assert.Equal("Fingerprinting: 120 of 340 · 12 min left", row.Detail);
        Assert.Equal(100.0 * 120 / 340, row.Progress, 3);
        Assert.False(row.IsIndeterminate);
        Assert.Equal(calls, sender.Calls);
    }

    [Fact]
    public async Task Progress_OfAGrowingTotal_IsIndeterminate_AndRetryingIsShownBeforeAnyFailure()
    {
        var running = Job("Import photos", ProcessingJobState.Running, total: 301, done: 240);
        sender.Queue = new ProcessingQueue([running], []);
        await panel.RefreshAsync(Token);
        panel.Attach();

        progress.Set(Summary(
            running,
            null,
            retrying: 3,
            new StageProgress("import.discover", 1, 0, 0, true),
            new StageProgress("import.fingerprint", 300, 240, 0, false)));

        var row = Assert.Single(panel.Active);
        Assert.True(row.IsIndeterminate);
        Assert.Equal("3 retrying", row.RetryingText);
        Assert.Null(row.FailedText);
    }

    [Fact]
    public async Task Progress_OfANewJob_ListsTheJobsAgain()
    {
        sender.Queue = new ProcessingQueue([], []);
        await panel.RefreshAsync(Token);
        panel.Attach();
        var started = Job("Rescan", ProcessingJobState.Running, total: 1, pending: 1);
        sender.Queue = new ProcessingQueue([started], []);

        progress.Set(Summary(started, null, 0, new StageProgress("rescan", 1, 0, 0, true)));
        await WaitUntilAsync(() => panel.Active.Count == 1);

        Assert.Equal("Rescan", Assert.Single(panel.Active).Description);
    }

    [Fact]
    public async Task Refresh_KeepsTheRowOfAJobThatMoves_AndDropsPrunedJobs()
    {
        var job = Job("Import photos", ProcessingJobState.Running, total: 10, done: 5);
        var old = Job("Old job", ProcessingJobState.Completed, total: 1, done: 1);
        sender.Queue = new ProcessingQueue([job], [old]);
        await panel.RefreshAsync(Token);
        var row = panel.Active[0];

        sender.Queue = new ProcessingQueue([], [job with { State = ProcessingJobState.Completed, FinishedAt = Now }]);
        await panel.RefreshAsync(Token);

        Assert.Empty(panel.Active);
        Assert.Same(row, Assert.Single(panel.Recent));
        Assert.True(row.IsFinished);
    }

    [Fact]
    public async Task Refresh_ShowsFailuresOfAFinishedJob()
    {
        var job = Job("Import photos", ProcessingJobState.Completed, total: 10, done: 7) with { Failed = 3, FinishedAt = Now };
        sender.Queue = new ProcessingQueue([], [job]);

        await panel.RefreshAsync(Token);

        var row = Assert.Single(panel.Recent);
        Assert.True(row.HasFailures);
        Assert.Equal("3 failed", row.FailedText);
    }

    [Fact]
    public void StatusBarIndicator_OpensThePanel_AndTheViewMenuHasACommandForIt()
    {
        var presenter = new ProcessingPanelPresenter(panel);
        var statusBar = new StatusBarViewModel([new ProcessingStatusSegment(feed, Localizer.Current, presenter)]);
        var registry = new CommandRegistry([new ProcessingCommands(presenter)], Localizer.Current, new FakeCapabilityGate());

        Assert.True(Assert.Single(statusBar.Right).IsClickable);
        Assert.Equal(CommandGroups.View, registry[ProcessingCommands.OpenQueue].Group);
    }

    [Fact]
    public async Task Close_AsksForTheWindowToClose_AndNeverTouchesTheJobs()
    {
        var requested = 0;
        panel.CloseRequested += (_, _) => requested++;
        await panel.RefreshAsync(Token);
        var calls = sender.Calls;

        panel.CloseCommand.Execute(null);

        Assert.Equal(1, requested);
        Assert.Equal(calls, sender.Calls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Token);
        }
    }

    private static QueueJob Job(string description, ProcessingJobState state, int total, int done = 0, int pending = 0) =>
        new(Id<ProcessingJob>.New(), "import", description, state, Now, Now, null, total, done, 0, pending == 0 ? total - done : pending);

    private static ProgressSummary Summary(QueueJob job, TimeSpan? remaining, int retrying, params StageProgress[] stages) =>
        new([new JobProgress(job.Id, job.Type, job.Description, stages, remaining) { Retrying = retrying }], remaining);

    private sealed class QueueSender : FakeSender
    {
        public ProcessingQueue Queue { get; set; } = ProcessingQueue.Empty;

        public int Calls { get; private set; }

        protected override object? Handle(object request)
        {
            Calls++;
            return request is GetProcessingQueueQuery ? Queue : throw new NotSupportedException();
        }
    }
}
