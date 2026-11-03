using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;

namespace MediaFileClassifier.Infrastructure.Tests.Libraries;

/// <summary>
/// Tests for <see cref="LibrarySession"/>: background processing follows the open library (E05-S01, E03-S07).
/// </summary>
public sealed class LibrarySessionTests
{
    [Fact]
    public async Task SetCurrentAsync_StopsProcessingBeforeClosingAndStartsItForTheNewLibrary()
    {
        await using var first = await LibraryTestBed.CreateAsync();
        await using var second = await LibraryTestBed.CreateAsync();
        var jobs = new RecordingJobSystem();
        var session = new LibrarySession(jobs);

        await session.SetCurrentAsync(first.Library);
        await session.SetCurrentAsync(second.Library);
        await session.DisposeAsync();
        await first.OpenAgainAsync();
        await second.OpenAgainAsync();

        Assert.Equal(["start first", "stop", "start second", "stop"], jobs.Calls.Select(call => call.Replace(first.Library.Layout.RootDirectory, "first").Replace(second.Library.Layout.RootDirectory, "second")));
    }

    private sealed class RecordingJobSystem : IJobSystem
    {
        public List<string> Calls { get; } = [];

        public IJobScheduler? Current => null;

        public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default)
        {
            Calls.Add($"start {library.Layout.RootDirectory}");
            return Task.FromResult<IJobScheduler>(null!);
        }

        public Task StopAsync()
        {
            Calls.Add("stop");
            return Task.CompletedTask;
        }

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
