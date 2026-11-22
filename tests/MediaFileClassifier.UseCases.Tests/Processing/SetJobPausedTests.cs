using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Processing.SetJobPaused;
using MediaFileClassifier.UseCases.Tests.Analysis;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Processing;

/// <summary>
/// Tests for pausing and resuming one job from the processing panel (E05-S05-T02).
/// </summary>
public sealed class SetJobPausedTests : IDisposable
{
    private readonly RecordingScheduler scheduler = new();
    private readonly ServiceProvider services;

    public SetJobPausedTests()
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
    public async Task PauseThenResume_AsksTheScheduler_AndReportsWhetherTheJobChanged()
    {
        var job = Id<ProcessingJob>.New();

        Assert.True(await Sender.Send(new SetJobPausedCommand(job, Paused: true), Token));
        Assert.Contains(job, scheduler.PausedJobs);
        Assert.False(await Sender.Send(new SetJobPausedCommand(job, Paused: true), Token));

        Assert.True(await Sender.Send(new SetJobPausedCommand(job, Paused: false), Token));
        Assert.Empty(scheduler.PausedJobs);
        Assert.False(await Sender.Send(new SetJobPausedCommand(job, Paused: false), Token));
    }

    [Fact]
    public async Task WithoutALibrary_ReturnsFalse()
    {
        using var closed = new ServiceCollection()
            .AddSingleton<IJobSystem>(new FakeJobSystem(null))
            .AddUseCases()
            .BuildServiceProvider();

        Assert.False(await closed.GetRequiredService<ISender>().Send(new SetJobPausedCommand(Id<ProcessingJob>.New(), Paused: true), Token));
    }
}
