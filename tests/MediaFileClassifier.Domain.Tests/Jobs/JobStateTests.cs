using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Tests.Jobs;

/// <summary>
/// Tests for <see cref="ProcessingJob"/>, <see cref="WorkItem"/> and their state transitions (E05-S01-T01).
/// </summary>
public sealed class JobStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WorkItemState.Queued, WorkItemState.Running)]
    [InlineData(WorkItemState.Queued, WorkItemState.Cancelled)]
    [InlineData(WorkItemState.Running, WorkItemState.Completed)]
    [InlineData(WorkItemState.Running, WorkItemState.Failed)]
    [InlineData(WorkItemState.Running, WorkItemState.Cancelled)]
    [InlineData(WorkItemState.Running, WorkItemState.Queued)]
    [InlineData(WorkItemState.Failed, WorkItemState.Queued)]
    [InlineData(WorkItemState.Cancelled, WorkItemState.Queued)]
    public void WorkItemTransitions_Allowed(WorkItemState from, WorkItemState to)
    {
        Assert.True(WorkItemStateTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(WorkItemState.Queued, WorkItemState.Completed)]
    [InlineData(WorkItemState.Queued, WorkItemState.Failed)]
    [InlineData(WorkItemState.Completed, WorkItemState.Queued)]
    [InlineData(WorkItemState.Completed, WorkItemState.Running)]
    [InlineData(WorkItemState.Failed, WorkItemState.Running)]
    [InlineData(WorkItemState.Running, WorkItemState.Running)]
    public void WorkItemTransitions_NotAllowed(WorkItemState from, WorkItemState to)
    {
        Assert.False(WorkItemStateTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void WorkItem_StartAndFail_CountsAttemptsAndRecordsError()
    {
        var item = WorkItem.Create(Id<ProcessingJob>.New(), Id<MediaItem>.New(), "thumbnail", ResourcePool.Cpu, (int)JobPriority.Import, null, Start);

        var failed = item.ChangeState(WorkItemState.Running, Start.AddSeconds(1)).ChangeState(WorkItemState.Failed, Start.AddSeconds(2), "corrupt");
        var retried = failed.ChangeState(WorkItemState.Queued, Start.AddSeconds(3)).ChangeState(WorkItemState.Running, Start.AddSeconds(4));

        Assert.Equal(WorkItemState.Failed, failed.State);
        Assert.Equal("corrupt", failed.LastError);
        Assert.Equal(Start.AddSeconds(2), failed.FinishedAt);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Null(retried.LastError);
        Assert.Null(retried.FinishedAt);
        Assert.Equal(Start.AddSeconds(4), retried.StartedAt);
    }

    [Fact]
    public void WorkItem_NotAllowedChange_Throws()
    {
        var item = WorkItem.Create(Id<ProcessingJob>.New(), null, "discover", ResourcePool.Io, (int)JobPriority.Import, "{}", Start);

        Assert.Throws<InvalidOperationException>(() => item.ChangeState(WorkItemState.Completed, Start));
    }

    [Fact]
    public void WorkItem_Create_NeedsAStage()
    {
        Assert.Throws<ArgumentException>(() => WorkItem.Create(Id<ProcessingJob>.New(), null, " ", ResourcePool.Io, 0, null, Start));
    }

    [Theory]
    [InlineData(ProcessingJobState.Queued, ProcessingJobState.Running)]
    [InlineData(ProcessingJobState.Queued, ProcessingJobState.WaitingForModel)]
    [InlineData(ProcessingJobState.Running, ProcessingJobState.Completed)]
    [InlineData(ProcessingJobState.Running, ProcessingJobState.Failed)]
    [InlineData(ProcessingJobState.Running, ProcessingJobState.Cancelled)]
    [InlineData(ProcessingJobState.WaitingForModel, ProcessingJobState.Queued)]
    [InlineData(ProcessingJobState.Completed, ProcessingJobState.Queued)]
    [InlineData(ProcessingJobState.Failed, ProcessingJobState.Queued)]
    public void JobTransitions_Allowed(ProcessingJobState from, ProcessingJobState to)
    {
        Assert.True(ProcessingJobStateTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(ProcessingJobState.Queued, ProcessingJobState.Completed)]
    [InlineData(ProcessingJobState.Completed, ProcessingJobState.Running)]
    [InlineData(ProcessingJobState.Cancelled, ProcessingJobState.Running)]
    [InlineData(ProcessingJobState.WaitingForModel, ProcessingJobState.Running)]
    public void JobTransitions_NotAllowed(ProcessingJobState from, ProcessingJobState to)
    {
        Assert.False(ProcessingJobStateTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void Job_ChangeState_RecordsStartOnceAndFinish()
    {
        var job = new ProcessingJob { Id = Id<ProcessingJob>.New(), Type = "import", Description = "Import", CreatedAt = Start };

        var running = job.ChangeState(ProcessingJobState.Running, Start.AddSeconds(1));
        var requeued = running.ChangeState(ProcessingJobState.Queued, Start.AddSeconds(2)).ChangeState(ProcessingJobState.Running, Start.AddSeconds(3));
        var failed = requeued.ChangeState(ProcessingJobState.Failed, Start.AddSeconds(4), "2 items failed");

        Assert.Equal(Start.AddSeconds(1), requeued.StartedAt);
        Assert.Null(requeued.FinishedAt);
        Assert.Equal(Start.AddSeconds(4), failed.FinishedAt);
        Assert.Equal("2 items failed", failed.LastError);
        Assert.Throws<InvalidOperationException>(() => failed.ChangeState(ProcessingJobState.Completed, Start));
    }
}
