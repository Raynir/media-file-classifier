using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Notifications;
using MediaFileClassifier.UseCases.Common.Notifications;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Rescan;
using MediaFileClassifier.UseCases.Rescan.GetRescanSummary;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Notifications;

/// <summary>
/// The summary of the quick sync on library open (E04-S03-T01): shown only when something changed.
/// </summary>
public sealed class QuickSyncNotificationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASyncThatFoundChanges_SaysSo()
    {
        var jobs = Create(new RescanCounts(Folders: 2, Offline: 0, New: 3, Moved: 1, Missing: 2, Modified: 4, Unchanged: 90));

        var notification = await jobs.DescribeQuickSyncAsync(Finished(RescanStages.QuickSyncJob, ProcessingJobState.Completed), Token);

        Assert.Equal("Library updated: 3 new, 1 moved, 2 missing, 4 changed", notification!.Message);
        Assert.Equal(NotificationSeverity.Info, notification.Severity);
    }

    [Fact]
    public async Task ASyncThatFoundNothing_SaysNothing()
    {
        var jobs = Create(new RescanCounts(Folders: 2, Offline: 1, New: 0, Moved: 0, Missing: 0, Modified: 0, Unchanged: 90));

        Assert.Null(await jobs.DescribeQuickSyncAsync(Finished(RescanStages.QuickSyncJob, ProcessingJobState.Completed), Token));
    }

    [Theory]
    [InlineData(RescanStages.RescanJob, ProcessingJobState.Completed)]
    [InlineData(ImportStages.ImportJob, ProcessingJobState.Completed)]
    [InlineData(RescanStages.QuickSyncJob, ProcessingJobState.Failed)]
    public async Task OtherJobsAndFailedSyncs_AreLeftToTheirOwnNotifications(string type, ProcessingJobState state)
    {
        var jobs = Create(new RescanCounts(Folders: 1, Offline: 0, New: 5, Moved: 0, Missing: 0, Modified: 0, Unchanged: 0));

        Assert.Null(await jobs.DescribeQuickSyncAsync(Finished(type, state), Token));
    }

    private static JobNotifications Create(RescanCounts counts)
    {
        var service = new NotificationService(new InMemorySettingsStore(), action => action());
        return new JobNotifications(new FakeJobProgress(), service, TestLocalizer.EnUs(), new Summaries(new RescanSummary(ProcessingJobState.Completed, counts, 0)));
    }

    private static JobFinishedEventArgs Finished(string type, ProcessingJobState state) =>
        new(new JobProgress(Id<ProcessingJob>.New(), type, "A job", [], null), state);

    private sealed class Summaries(RescanSummary summary) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Assert.IsType<GetRescanSummaryQuery>(request);
            return Task.FromResult((TResponse)(object)summary);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
    }
}
