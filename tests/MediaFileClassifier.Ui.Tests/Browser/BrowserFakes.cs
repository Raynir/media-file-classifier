using Avalonia.Threading;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.ThumbnailTileSize;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Results made up on the fly, answered synchronously, so the grid can be tested with 100k of them.
/// </summary>
internal sealed class FakeResultSource : IMediaResultSource
{
    private static readonly DateTimeOffset Modified = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<Id<MediaItem>> ids = [];
    private DateTimeOffset clock = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public FakeResultSource(int count) => Resize(count);

    public event EventHandler? Changed;

    public int PageRequests { get; private set; }

    public int IdRequests { get; private set; }

    /// <summary>
    /// Gets or sets a task the existence check waits for, to test gestures made while a refresh runs.
    /// </summary>
    public Task ExistingGate { get; set; } = Task.CompletedTask;

    public Func<int, MediaItemSummary>? Override { get; set; }

    public IReadOnlyList<Id<MediaItem>> Ids => ids;

    public static MediaItemSummary Summary(Id<MediaItem> id, int index, MediaItemStatus status = MediaItemStatus.Indexed) =>
        new(id, $"IMG_{index:D6}.jpg", "jpg", MediaType.Image, 2048, Modified, status, null, null, false);

    /// <summary>
    /// Inserts new results at the top, as an import does in newest-first order.
    /// </summary>
    public void InsertAtTop(int count)
    {
        ids.InsertRange(0, NewIds(count));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resize(int count)
    {
        ids.Clear();
        ids.AddRange(NewIds(count));
    }

    /// <summary>
    /// Removes results, as deleting items does.
    /// </summary>
    public void Remove(params Id<MediaItem>[] removed)
    {
        ids.RemoveAll(removed.Contains);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(ids.Count);

    public Task<IReadOnlyList<MediaItemSummary>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        PageRequests++;
        return Task.FromResult<IReadOnlyList<MediaItemSummary>>(
            [.. Enumerable.Range(offset, Math.Max(0, Math.Min(limit, ids.Count - offset))).Select(index => Override?.Invoke(index) ?? Summary(ids[index], index))]);
    }

    public Task<int> PositionOfAsync(Id<MediaItem> itemId, CancellationToken cancellationToken) => Task.FromResult(ids.IndexOf(itemId));

    public Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken)
    {
        IdRequests++;
        var start = newest is { } boundary ? ids.FindIndex(id => id.Value.CompareTo(boundary.Value) <= 0) : 0;
        return Task.FromResult<IReadOnlyList<Id<MediaItem>>>(start < 0 ? [] : [.. ids.Skip(start + offset).Take(limit)]);
    }

    public async Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> wanted, CancellationToken cancellationToken)
    {
        var existing = ids.Where(wanted.Contains).ToHashSet();
        await ExistingGate;
        return existing;
    }

    // Newest first, with UUIDv7 IDs a millisecond apart, so ID order is result order as in a library.
    private List<Id<MediaItem>> NewIds(int count)
    {
        var created = Enumerable.Range(0, count).Select(_ => new Id<MediaItem>(Guid.CreateVersion7(clock = clock.AddMilliseconds(1)))).ToList();
        created.Reverse();
        return created;
    }
}

/// <summary>
/// A thumbnail cache whose files the test sets.
/// </summary>
internal sealed class FakeThumbnailCache : IThumbnailCache
{
    public Dictionary<(Id<MediaItem> Id, ThumbnailSize Size), string> Files { get; } = [];

    public string? Find(LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt) =>
        Files.GetValueOrDefault((itemId, size));

    public Task<ThumbnailOutcome> GenerateAsync(LibraryLayout library, MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public void Invalidate(LibraryLayout library, Id<MediaItem> itemId) => throw new NotSupportedException();
}

/// <summary>
/// A session with an open library that only has a layout, or with none.
/// </summary>
internal sealed class FakeBrowserSession(bool open = true) : ILibrarySession
{
    public event EventHandler? CurrentChanged;

    public IOpenLibrary? Current { get; private set; } = open ? new LayoutOnlyLibrary() : null;

    public Task SetCurrentAsync(IOpenLibrary? library)
    {
        Current = library;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private sealed class LayoutOnlyLibrary : IOpenLibrary
    {
        public LibraryLayout Layout { get; } = LibraryLayout.For(Path.GetTempPath(), "browser-tests");

        public Library Library { get; } = Library.Create("browser-tests", DateTimeOffset.UnixEpoch);

        public IFolderRepository Folders => throw new NotSupportedException();

        public IMediaItemRepository Items => throw new NotSupportedException();

        public IItemMetadataRepository Metadata => throw new NotSupportedException();

        public IModelRegistrationRepository Models => throw new NotSupportedException();

        public IJobRepository Jobs => throw new NotSupportedException();

        public MediaFileClassifier.Infrastructure.Abstractions.Repositories.IEmbeddingRepository Embeddings => throw new NotSupportedException();

        public MediaFileClassifier.Infrastructure.Abstractions.Search.ISearchStore Search => throw new NotSupportedException();

        public ILibraryMaintenance Maintenance => throw new NotSupportedException();

        public Task UpdateLibraryAsync(Library library, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// A job system whose scheduler records the boosts.
/// </summary>
internal sealed class FakeJobSystem : IJobSystem
{
    private readonly BoostRecorder scheduler = new();

    public IJobScheduler? Current => scheduler;

    public List<IReadOnlyCollection<Id<MediaItem>>> Boosts => scheduler.Boosts;

    public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task StopAsync() => Task.CompletedTask;

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    private sealed class BoostRecorder : IJobScheduler
    {
        public List<IReadOnlyCollection<Id<MediaItem>>> Boosts { get; } = [];

        public IOpenLibrary Library => throw new NotSupportedException();

        public bool IsPaused => false;

        public void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds) => Boosts.Add([.. itemIds]);

        public Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public int GetPoolLimit(ResourcePool pool) => 1;

        public void SetPoolLimit(ResourcePool pool, int limit)
        {
        }

        public void Resume()
        {
        }
    }
}

/// <summary>
/// Answers the tile size requests from memory, synchronously.
/// </summary>
internal sealed class FakeTileSizeSender : ISender
{
    public int? StoredSize { get; set; } = LibrarySettings.DefaultThumbnailTileSize;

    public List<int> Saved { get; } = [];

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => request switch
    {
        GetThumbnailTileSizeQuery => Task.FromResult((TResponse)(object)StoredSize!),
        _ => throw new NotSupportedException(),
    };

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        if (request is SetThumbnailTileSizeCommand command)
        {
            Saved.Add(command.Size);
            StoredSize = command.Size;
            return Task.CompletedTask;
        }

        throw new NotSupportedException();
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Records the items opened.
/// </summary>
internal sealed class FakeItemOpener : IItemOpener
{
    public List<Id<MediaItem>> Opened { get; } = [];

    public Task<bool> OpenAsync(Id<MediaItem> itemId)
    {
        Opened.Add(itemId);
        return Task.FromResult(true);
    }
}

/// <summary>
/// Runs the UI thread's queued work until a condition holds, for work that finishes on the thread pool.
/// </summary>
internal static class Pump
{
    public static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting on the UI thread.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
}
