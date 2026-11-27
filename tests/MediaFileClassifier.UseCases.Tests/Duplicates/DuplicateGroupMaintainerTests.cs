using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Duplicates;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="DuplicateGroupMaintainer"/> (E12-S01-T02): when it reconciles, and that it follows the session.
/// </summary>
public sealed class DuplicateGroupMaintainerTests : IAsyncDisposable
{
    private readonly FakeLibrarySession session = new();
    private readonly FakeOpenLibrary library = new(LibraryLayout.For(Path.GetTempPath(), "x"), "x");
    private readonly QueueRepository queue = new();
    private readonly DuplicateGroupMaintainer maintainer;

    public DuplicateGroupMaintainerTests()
    {
        library.DuplicateList = queue;
        maintainer = new DuplicateGroupMaintainer(session, TimeProvider.System, NullLogger<DuplicateGroupMaintainer>.Instance, delay: TimeSpan.Zero);
    }

    [Fact]
    public async Task Open_LibraryWithQueuedHashes_ReconcilesUntilTheQueueIsEmpty()
    {
        queue.Queued = 1200;

        await session.SetCurrentAsync(library);
        maintainer.Start();
        await maintainer.IdleAsync();

        Assert.Equal(0, queue.Queued);
        Assert.Equal([500, 500, 500, 500], queue.Limits.Take(4));
    }

    [Fact]
    public async Task ItemsChanged_ReconcilesAgain()
    {
        maintainer.Start();
        await session.SetCurrentAsync(library);
        await maintainer.IdleAsync();
        var calls = queue.Limits.Count;

        queue.Queued = 3;
        library.ItemList.RaiseChanged();
        await maintainer.IdleAsync();

        Assert.Equal(0, queue.Queued);
        Assert.True(queue.Limits.Count > calls);
    }

    [Fact]
    public async Task LibraryClosed_StopsListening()
    {
        maintainer.Start();
        await session.SetCurrentAsync(library);
        await maintainer.IdleAsync();

        await session.SetCurrentAsync(null);
        queue.Queued = 3;
        library.ItemList.RaiseChanged();
        await maintainer.IdleAsync();

        Assert.Equal(0, library.ItemList.ChangedHandlerCount);
        Assert.Equal(3, queue.Queued);
    }

    [Fact]
    public async Task ReconcileFails_TheNextChangeTriesAgain()
    {
        queue.Queued = 3;
        queue.Failures = 1;
        maintainer.Start();
        await session.SetCurrentAsync(library);
        await maintainer.IdleAsync();
        Assert.Equal(3, queue.Queued);

        library.ItemList.RaiseChanged();
        await maintainer.IdleAsync();

        Assert.Equal(0, queue.Queued);
    }

    public async ValueTask DisposeAsync()
    {
        maintainer.Dispose();
        await library.DisposeAsync();
    }

    /// <summary>
    /// A queue of content hashes that reconciling takes from.
    /// </summary>
    private sealed class QueueRepository : IDuplicateGroupRepository
    {
        public event EventHandler? Changed;

        public int Queued { get; set; }

        public int Failures { get; set; }

        public ConcurrentQueue<int> LimitQueue { get; } = new();

        public List<int> Limits => [.. LimitQueue];

        public Task<DuplicateReconcileResult> ReconcileAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            LimitQueue.Enqueue(limit);
            if (Failures > 0)
            {
                Failures--;
                throw new InvalidOperationException("Disk full");
            }

            var taken = Math.Min(limit, Queued);
            Queued -= taken;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(taken == 0 ? DuplicateReconcileResult.Empty : new DuplicateReconcileResult(taken, taken, 0, 0, 0));
        }

        public Task<DuplicateGroup?> GetAsync(Id<DuplicateGroup> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DuplicateGroup>> GetGroupsAsync(DuplicateGroupState? state, int offset, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountAsync(DuplicateGroupState? state, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DuplicateGroupSummary>> GetSummariesAsync(DuplicateGroupState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DuplicateStatistics> GetOpenStatisticsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DuplicateGroupMember>> GetMembersAsync(Id<DuplicateGroup> groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ChangeStateAsync(Id<DuplicateGroup> id, DuplicateGroupState state, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> QueueAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountQueuedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Queued);
    }
}
