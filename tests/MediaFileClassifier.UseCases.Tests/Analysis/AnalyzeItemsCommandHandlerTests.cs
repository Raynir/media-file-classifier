using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalyzeItemsCommandHandler"/>: the job it queues (E06-S01, E09-S03-T03).
/// </summary>
public sealed class AnalyzeItemsCommandHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, JobPriority.AiAnalysis)]
    [InlineData(JobPriority.UserAction, JobPriority.UserAction)]
    public async Task Handle_QueuesTheJob_WithTheRequestedPriority(JobPriority? requested, JobPriority expected)
    {
        var scheduler = new RecordingScheduler();
        var handler = new AnalyzeItemsCommandHandler(new FakeJobSystem(scheduler), NullLogger<AnalyzeItemsCommandHandler>.Instance);
        var itemId = Id<MediaItem>.New();
        var command = requested is { } priority
            ? new AnalyzeItemsCommand([itemId], Priority: priority)
            : new AnalyzeItemsCommand([itemId]);

        var jobId = await handler.Handle(command, Token);

        Assert.NotNull(jobId);
        var request = Assert.Single(scheduler.Requests);
        Assert.Equal(expected, request.Priority);
        Assert.Equal([itemId], request.ItemIds);
    }

    [Fact]
    public async Task Handle_ItemAnalyzers_GiveEachItemItsOwnPayload_AndLeaveOutItemsWithNone()
    {
        var scheduler = new RecordingScheduler();
        var handler = new AnalyzeItemsCommandHandler(new FakeJobSystem(scheduler), NullLogger<AnalyzeItemsCommandHandler>.Instance);
        Id<MediaItem> describe = Id<MediaItem>.New(), both = Id<MediaItem>.New(), none = Id<MediaItem>.New();
        var command = new AnalyzeItemsCommand([describe, both, none])
        {
            ItemAnalyzers = new Dictionary<Id<MediaItem>, IReadOnlyList<string>>
            {
                [describe] = ["describer"],
                [both] = ["embedding", "describer"],
                [none] = [],
            },
        };

        await handler.Handle(command, Token);

        var request = Assert.Single(scheduler.Requests);
        Assert.Equal([describe, both], request.ItemIds);
        Assert.Equal(
            ["{\"analyzers\":[\"describer\"]}", "{\"analyzers\":[\"embedding\",\"describer\"]}"],
            request.WorkItems.Select(work => work.Payload));
    }

    [Fact]
    public async Task Handle_WithoutBackgroundProcessing_QueuesNothing()
    {
        var handler = new AnalyzeItemsCommandHandler(new FakeJobSystem(null), NullLogger<AnalyzeItemsCommandHandler>.Instance);

        var jobId = await handler.Handle(new AnalyzeItemsCommand([Id<MediaItem>.New()], Priority: JobPriority.UserAction), Token);

        Assert.Null(jobId);
    }
}
