using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Processing.CancelJob;
using MediaFileClassifier.UseCases.Tests.Analysis;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Processing;

/// <summary>
/// Tests for cancelling a job from the processing panel (E05-S03-T02).
/// </summary>
public sealed class CancelJobTests : IDisposable
{
    private readonly CancellingScheduler scheduler = new();
    private readonly ServiceProvider services;

    public CancelJobTests()
    {
        services = new ServiceCollection()
            .AddSingleton<IJobSystem>(new FakeJobSystem(scheduler))
            .AddUseCases()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Cancel_AnUnfinishedJob_AsksTheSchedulerAndReturnsTrue()
    {
        var job = Id<ProcessingJob>.New();
        scheduler.Cancellable.Add(job);

        Assert.True(await Sender.Send(new CancelJobCommand(job), Token));
        Assert.Equal([job], scheduler.Asked);
    }

    [Fact]
    public async Task Cancel_AFinishedJob_ReturnsFalse()
    {
        var job = Id<ProcessingJob>.New();

        Assert.False(await Sender.Send(new CancelJobCommand(job), Token));
        Assert.Equal([job], scheduler.Asked);
    }

    [Fact]
    public async Task Cancel_WithoutALibrary_ReturnsFalseAndAsksNobody()
    {
        using var closed = new ServiceCollection()
            .AddSingleton<IJobSystem>(new FakeJobSystem(null))
            .AddUseCases()
            .BuildServiceProvider();

        Assert.False(await closed.GetRequiredService<ISender>().Send(new CancelJobCommand(Id<ProcessingJob>.New()), Token));
        Assert.Empty(scheduler.Asked);
    }

    private sealed class CancellingScheduler : IJobScheduler
    {
        public HashSet<Id<ProcessingJob>> Cancellable { get; } = [];

        public List<Id<ProcessingJob>> Asked { get; } = [];

        public IOpenLibrary Library => throw new NotSupportedException();

        public bool IsPaused => false;

        public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default)
        {
            Asked.Add(jobId);
            return Task.FromResult(Cancellable.Contains(jobId));
        }

        public Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> AppendWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds) => throw new NotSupportedException();

        public int GetPoolLimit(ResourcePool pool) => 1;

        public void SetPoolLimit(ResourcePool pool, int limit)
        {
        }

        public void Resume()
        {
        }

        public event EventHandler? PauseChanged
        {
            add { }
            remove { }
        }

        public void Pause()
        {
        }

        public bool IsPoolPaused(ResourcePool pool) => false;

        public void PausePool(ResourcePool pool)
        {
        }

        public void ResumePool(ResourcePool pool)
        {
        }

        public Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public void ReleaseParked()
        {
        }
    }
}
