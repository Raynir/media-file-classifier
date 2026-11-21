using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Notifications;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Import;

namespace MediaFileClassifier.Ui.Tests.Notifications;

/// <summary>
/// Tests for <see cref="JobNotifications"/>: one summary per finished job (E15-S01-T02).
/// </summary>
public sealed class JobNotificationsTests
{
    private readonly FakeJobProgress progress = new();
    private readonly List<Notification> raised = [];
    private readonly JobNotifications jobs;

    public JobNotificationsTests()
    {
        var service = new NotificationService(new InMemorySettingsStore(), action => action());
        service.Raised += (_, notification) => raised.Add(notification);
        jobs = new JobNotifications(progress, service, TestLocalizer.EnUs());
        jobs.Start();
        jobs.Start();
    }

    [Fact]
    public void AnImportOfAThousandItems_RaisesExactlyOneNotification_WithItsAction()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Completed, Stage("discover", 1, 1, 0, whole: true), Stage("fingerprint", 1_000, 1_000, 0), Stage("media", 1_000, 1_000, 0)));

        var notification = Assert.Single(raised);
        Assert.Equal($"Imported {Format.Number(1_000)} items", notification.Message);
        Assert.Equal(NotificationSeverity.Success, notification.Severity);
        Assert.Equal(new NotificationAction("Show imported items", JobNotifications.ImportedItemsCommand), notification.Action);
    }

    [Fact]
    public void ASingleItem_UsesTheSingularText()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Completed, Stage("fingerprint", 1, 1, 0)));

        Assert.Equal("Imported 1 item", Assert.Single(raised).Message);
    }

    [Fact]
    public void AnImportWithFailures_IsAWarning_WithViewFailedItems()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Completed, Stage("fingerprint", 1_240, 1_237, 3)));

        var notification = Assert.Single(raised);
        Assert.Equal($"Import finished: {Format.Number(1_237)} imported, 3 failed", notification.Message);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Equal(new NotificationAction("View failed items", ProcessingCommands.OpenQueue), notification.Action);
    }

    [Fact]
    public void AJobWhoseEveryItemFailed_IsAnError()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Failed, Stage("fingerprint", 5, 0, 5)));

        var notification = Assert.Single(raised);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        Assert.Equal("Import failed: no item could be imported (5 failed)", notification.Message);
    }

    [Fact]
    public void AnAnalysisJob_ReportsItsItemsOnce_AcrossItsAnalyzerStages()
    {
        progress.Finish(Finished(AnalysisStages.AnalysisJob, ProcessingJobState.Completed, Stage("embedding", 1_240, 1_240, 0), Stage("describer", 1_240, 1_240, 0), Stage("ocr", 900, 900, 0)));

        var notification = Assert.Single(raised);
        Assert.Equal($"Analysis finished: {Format.Number(1_240)} items", notification.Message);
        Assert.Null(notification.Action);
    }

    [Fact]
    public void AnAnalysisWithFailures_CapsFailuresAcrossStagesAtTheItemCount()
    {
        progress.Finish(Finished(AnalysisStages.AnalysisJob, ProcessingJobState.Completed, Stage("embedding", 10, 6, 4), Stage("describer", 10, 6, 4), Stage("ocr", 10, 2, 8)));

        var notification = Assert.Single(raised);
        Assert.Equal("Analysis failed: no item could be analysed (10 failed)", notification.Message);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
    }

    [Theory]
    [InlineData("fullhash")]
    [InlineData("rescan")]
    [InlineData("cleanup")]
    public void OtherJobTypes_SayNothing(string type)
    {
        progress.Finish(Finished(type, ProcessingJobState.Completed, Stage("stage", 10, 10, 0)));

        Assert.Empty(raised);
    }

    [Fact]
    public void AJobWithNoItems_SaysNothing()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Completed, Stage("discover", 1, 1, 0, whole: true)));

        Assert.Empty(raised);
    }

    [Fact]
    public void TwoJobs_RaiseTwoNotifications()
    {
        progress.Finish(Finished(ImportStages.ImportJob, ProcessingJobState.Completed, Stage("fingerprint", 3, 3, 0)));
        progress.Finish(Finished(AnalysisStages.AnalysisJob, ProcessingJobState.Completed, Stage("embedding", 3, 3, 0)));

        Assert.Equal(2, raised.Count);
    }

    private static StageProgress Stage(string stage, int total, int done, int failed, bool whole = false) => new(stage, total, done, failed, whole);

    private static JobFinishedEventArgs Finished(string type, ProcessingJobState state, params StageProgress[] stages) =>
        new(new JobProgress(Id<ProcessingJob>.New(), type, "A job", stages, null), state);
}
