using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Processing.GetProcessingQueue;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Processing;

/// <summary>
/// Tests for the job list of the processing panel (E05-S03-T01).
/// </summary>
public sealed class GetProcessingQueueTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public GetProcessingQueueTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Query_WithoutLibrary_IsEmpty() =>
        Assert.Same(ProcessingQueue.Empty, await Sender.Send(new GetProcessingQueueQuery(), Token));

    [Fact]
    public async Task Query_CountsWorkPerJob_WithoutCancelledItems_AndUsesTheFailuresThatNeedAttention()
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "queue"), "queue");
        var running = Job("Import photos", ProcessingJobState.Running);
        var finished = Job("Analyze 5 items", ProcessingJobState.Completed) with { FinishedAt = Now };
        library.JobList.Unfinished.Add(running);
        library.JobList.Recent.Add(finished);
        library.JobList.Counts.AddRange(
        [
            new WorkItemCount(running.Id, "import.fingerprint", WorkItemState.Completed, false, 7),
            new WorkItemCount(running.Id, "import.fingerprint", WorkItemState.Running, false, 1),
            new WorkItemCount(running.Id, "import.fingerprint", WorkItemState.Queued, false, 2),
            new WorkItemCount(running.Id, "import.fingerprint", WorkItemState.Failed, false, 3),
            new WorkItemCount(running.Id, "import.fingerprint", WorkItemState.Cancelled, false, 9),
            new WorkItemCount(finished.Id, "analysis.item", WorkItemState.Completed, false, 5),
        ]);

        // Of the three failed work items, only one is still in error.
        library.JobList.Failures[running.Id] = 1;
        await session.SetCurrentAsync(library);

        var queue = await Sender.Send(new GetProcessingQueueQuery(), Token);

        var active = Assert.Single(queue.Unfinished);
        Assert.Equal((13, 7, 1, 3), (active.Total, active.Done, active.Failed, active.Pending));
        Assert.False(active.IsFinished);
        var recent = Assert.Single(queue.Recent);
        Assert.Equal((5, 5, 0, 0), (recent.Total, recent.Done, recent.Failed, recent.Pending));
        Assert.True(recent.IsFinished);
    }

    [Fact]
    public async Task Query_ReturnsOnlyTheRecentLimit()
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "queue-limit"), "queue-limit");
        library.JobList.Recent.AddRange(Enumerable.Range(0, 5).Select(i => Job($"Job {i}", ProcessingJobState.Completed)));
        await session.SetCurrentAsync(library);

        var queue = await Sender.Send(new GetProcessingQueueQuery(RecentLimit: 2), Token);

        Assert.Equal(["Job 0", "Job 1"], queue.Recent.Select(job => job.Description));
    }

    private static ProcessingJob Job(string description, ProcessingJobState state) =>
        new() { Id = Id<ProcessingJob>.New(), Type = "import", Description = description, State = state, CreatedAt = Now };
}
