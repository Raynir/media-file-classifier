using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediaFileClassifier.UseCases.Tests.Analysis;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Search;

/// <summary>
/// Tests for <see cref="AnalyzeNow"/> (E09-S03-T03): "Find similar" on an item without an embedding queues it at user
/// priority and shows the results once the embedding lands, or says why it can't.
/// </summary>
public sealed class AnalyzeNowTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;
    private readonly StubSender sender;
    private readonly SearchResultSource source;
    private readonly FakeEmbeddings embeddings = new();
    private readonly FakeAnalyzerStates analyzerStates = new();
    private readonly ManualClock clock = new(Now);
    private readonly AnalyzeNow analyzeNow;
    private FakeOpenLibrary? library;

    public AnalyzeNowTests()
    {
        services = new ServiceCollection().AddSingleton<ILibrarySession>(session).AddUseCases().BuildServiceProvider();
        sender = new StubSender(services.GetRequiredService<ISender>());
        source = new SearchResultSource(services.GetRequiredService<LibraryItemsSource>(), sender);
        analyzeNow = new AnalyzeNow(source, sender, session, clock: clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (library is not null)
        {
            await library.DisposeAsync();
        }

        await services.DisposeAsync();
    }

    [Fact]
    public async Task UnanalyzedItem_OffersIt_QueuesTheWholeProfileAtUserPriority_AndSearchesOnceTheEmbeddingLands()
    {
        var item = await FindSimilarToUnanalyzedAsync();
        Assert.Equal(AnalyzeNowState.Offered, analyzeNow.State);

        await analyzeNow.AnalyzeAsync(Token);

        Assert.Equal(AnalyzeNowState.Analyzing, analyzeNow.State);
        var command = Assert.Single(sender.Analyses);
        Assert.Equal([item], command.ItemIds);
        Assert.Null(command.Analyzers);
        Assert.Equal(JobPriority.UserAction, command.Priority);

        // The embedding lands: the search starts again by itself, and finds the item analyzed.
        sender.Semantic = SemanticSearchStatus.Used;
        var restarted = 0;
        source.Changed += (_, _) => restarted++;
        embeddings.Commit(item);
        Assert.Equal(1, restarted);
        Assert.Equal(item, source.SimilarTo?.ItemId);
        await source.CountAsync(Token);

        Assert.Equal(AnalyzeNowState.Hidden, analyzeNow.State);
        Assert.Equal(0, embeddings.HandlerCount);
    }

    [Fact]
    public async Task EmbeddingDoneAfterItsCommit_KeepsAnalyzingUntilTheSearchFindsIt()
    {
        var item = await FindSimilarToUnanalyzedAsync();
        await analyzeNow.AnalyzeAsync(Token);
        embeddings.Commit(item);

        // The item changes before the search that runs again has counted: the offer doesn't come back meanwhile.
        analyzerStates.Set(AnalyzerState.Done(item, AnalyzerKeys.Embedding, null, Now.AddSeconds(5)));
        library!.ItemList.RaiseChanged();
        await analyzerStates.Read.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await Task.Delay(50, Token);
        Assert.Equal(AnalyzeNowState.Analyzing, analyzeNow.State);

        sender.Semantic = SemanticSearchStatus.Used;
        await source.CountAsync(Token);
        Assert.Equal(AnalyzeNowState.Hidden, analyzeNow.State);
    }

    [Fact]
    public async Task JobEndsWithoutAnEmbedding_SaysSo()
    {
        // E.g. an Indexed item whose embedding is skipped again: the item doesn't change, only the job ends.
        await FindSimilarToUnanalyzedAsync();
        await analyzeNow.AnalyzeAsync(Token);
        Assert.Equal(AnalyzeNowState.Analyzing, analyzeNow.State);

        library!.JobList.JobState = ProcessingJobState.Completed;
        library.ItemList.RaiseChanged();

        await WaitForStateAsync(AnalyzeNowState.Failed);
    }

    [Fact]
    public async Task AnalyzeAgainWhileAnalyzing_QueuesOnce()
    {
        await FindSimilarToUnanalyzedAsync();

        await analyzeNow.AnalyzeAsync(Token);
        await analyzeNow.AnalyzeAsync(Token);

        Assert.Single(sender.Analyses);
    }

    [Theory]
    [InlineData(AnalyzerStatus.Failed)]
    [InlineData(AnalyzerStatus.Skipped)]
    public async Task EmbeddingFailsOrIsSkipped_SaysSo_InsteadOfWaiting(AnalyzerStatus status)
    {
        var item = await FindSimilarToUnanalyzedAsync();
        await analyzeNow.AnalyzeAsync(Token);

        analyzerStates.Set(status == AnalyzerStatus.Failed
            ? AnalyzerState.Failed(item, AnalyzerKeys.Embedding, "model_error", null, "Permanent", isRetryable: true, Now.AddSeconds(5))
            : AnalyzerState.Skipped(item, AnalyzerKeys.Embedding, "model_missing", isRetryable: true, Now.AddSeconds(5)));
        library!.ItemList.RaiseChanged();

        await WaitForStateAsync(AnalyzeNowState.Failed);

        // The next search of the item still finds no embedding, and the outcome stays until the user tries again.
        await source.CountAsync(Token);
        Assert.Equal(AnalyzeNowState.Failed, analyzeNow.State);
        clock.Now = Now.AddSeconds(10);
        await analyzeNow.AnalyzeAsync(Token);
        Assert.Equal(AnalyzeNowState.Analyzing, analyzeNow.State);
        Assert.Equal(2, sender.Analyses.Count);
    }

    [Fact]
    public async Task AFailureFromBeforeTheClick_IsNotThisRunsOutcome()
    {
        var item = await FindSimilarToUnanalyzedAsync();
        analyzerStates.Set(AnalyzerState.Failed(item, AnalyzerKeys.Embedding, "model_error", null, "Permanent", isRetryable: true, Now.AddMinutes(-5)));
        await analyzeNow.AnalyzeAsync(Token);

        library!.ItemList.RaiseChanged();
        await analyzerStates.Read.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await Task.Delay(50, Token);

        Assert.Equal(AnalyzeNowState.Analyzing, analyzeNow.State);
    }

    [Fact]
    public async Task BackgroundProcessingNotRunning_SaysSoAtOnce()
    {
        await FindSimilarToUnanalyzedAsync();
        sender.JobQueued = false;

        await analyzeNow.AnalyzeAsync(Token);

        Assert.Equal(AnalyzeNowState.NotRunning, analyzeNow.State);
        Assert.Equal(0, embeddings.HandlerCount);
    }

    [Fact]
    public async Task ProcessingPaused_QueuesIt_AndSaysProcessingIsPaused()
    {
        var paused = new AnalyzeNow(source, sender, session, new FakeJobSystem(new RecordingScheduler { IsPaused = true }), clock: clock);
        await FindSimilarToUnanalyzedAsync();

        await paused.AnalyzeAsync(Token);

        Assert.Equal(AnalyzeNowState.Paused, paused.State);
        Assert.Single(sender.Analyses);
    }

    [Fact]
    public async Task LeavingSimilarityMode_HidesIt_AndStopsWatching()
    {
        var item = await FindSimilarToUnanalyzedAsync();
        var itemHandlers = library!.ItemList.ChangedHandlerCount;
        await analyzeNow.AnalyzeAsync(Token);
        Assert.Equal(itemHandlers + 1, library.ItemList.ChangedHandlerCount);

        source.ClearSimilar();

        Assert.Equal(AnalyzeNowState.Hidden, analyzeNow.State);
        Assert.Equal(0, embeddings.HandlerCount);
        Assert.Equal(itemHandlers, library.ItemList.ChangedHandlerCount);
        var restarted = 0;
        source.Changed += (_, _) => restarted++;
        embeddings.Commit(item);
        Assert.Equal(0, restarted);
    }

    [Fact]
    public async Task AnalyzedItem_OffersNothing()
    {
        await OpenLibraryAsync();
        var item = library!.ItemList.Items[0];
        source.SetSimilarTo(new SimilarSource(item.Id, "IMG_0.jpg"));
        await source.CountAsync(Token);

        Assert.Equal(AnalyzeNowState.Hidden, analyzeNow.State);
    }

    private async Task<Id<MediaItem>> FindSimilarToUnanalyzedAsync()
    {
        await OpenLibraryAsync();
        var item = library!.ItemList.Items[0];
        sender.Semantic = SemanticSearchStatus.NotAnalyzed;
        source.SetSimilarTo(new SimilarSource(item.Id, "IMG_0.jpg"));
        await source.CountAsync(Token);
        return item.Id;
    }

    private async Task OpenLibraryAsync()
    {
        library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "analyze-now"), "analyze-now")
        {
            EmbeddingList = embeddings,
            AnalyzerStateList = analyzerStates,
        };
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        for (var number = 0; number < 2; number++)
        {
            library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From($"album/IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now));
        }

        library.JobList.JobState = ProcessingJobState.Running;
        await session.SetCurrentAsync(library);
    }

    private async Task WaitForStateAsync(AnalyzeNowState expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (analyzeNow.State != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Token);
        }

        Assert.Equal(expected, analyzeNow.State);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeEmbeddings : IEmbeddingRepository
    {
        private EventHandler<EmbeddingsChangedEventArgs>? changed;

        public event EventHandler<EmbeddingsChangedEventArgs>? Changed
        {
            add => changed += value;
            remove => changed -= value;
        }

        public int HandlerCount => changed?.GetInvocationList().Length ?? 0;

        public void Commit(Id<MediaItem> itemId)
        {
            var modelId = Id<ModelRegistration>.New();
            var embedding = new Embedding(Id<Embedding>.New(), itemId, modelId, EmbeddingKind.Image, null, 2, new float[] { 1, 0 }, Now);
            changed?.Invoke(this, new EmbeddingsChangedEventArgs([new CommittedEmbeddings(1, new ItemEmbeddings(itemId, modelId, [embedding]))]));
        }

        public Task ReplaceAsync(Id<MediaItem> itemId, Id<ModelRegistration> modelId, IReadOnlyList<Embedding> embeddings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReplaceRangeAsync(IReadOnlyList<ItemEmbeddings> changes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Embedding>> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReadAllVectorsAsync(StoredVectorVisitor visitor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAnalyzerStates : IAnalyzerStateRepository
    {
        private readonly Lock gate = new();
        private readonly List<AnalyzerState> states = [];

        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Set(AnalyzerState state)
        {
            lock (gate)
            {
                states.RemoveAll(existing => existing.ItemId == state.ItemId && existing.Analyzer == state.Analyzer);
                states.Add(state);
            }
        }

        public Task<IReadOnlyList<AnalyzerState>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AnalyzerState> found;
            lock (gate)
            {
                found = [.. states.Where(state => state.ItemId == itemId)];
            }

            Read.TrySetResult();
            return Task.FromResult(found);
        }

        public Task SetAsync(IReadOnlyCollection<AnalyzerState> states, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Id<MediaItem>>> GetItemsAsync(string analyzer, AnalyzerStatus status, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OutdatedAnalysis> GetOutdatedAsync(IReadOnlyCollection<AnalyzerProvenance> current, CancellationToken cancellationToken = default) =>
            Task.FromResult(OutdatedAnalysis.None);

        public Task<RunApplication> ApplyRunAsync(
            AnalysisRun run,
            IReadOnlyCollection<AiFieldValue> values,
            IReadOnlyCollection<TagAssignment> tags,
            int retainedPreviousRuns,
            AnalyzerState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Answers <see cref="FindSimilarQuery"/> with no hits and <see cref="AnalyzeItemsCommand"/> with a job, and passes
    /// the other requests on.
    /// </summary>
    private sealed class StubSender(ISender inner) : ForwardingSender(inner)
    {
        public SemanticSearchStatus Semantic { get; set; } = SemanticSearchStatus.Used;

        public bool JobQueued { get; set; } = true;

        public List<AnalyzeItemsCommand> Analyses { get; } = [];

        public override async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            switch (request)
            {
                case FindSimilarQuery:
                    return (TResponse)(object)new MediaSearchResults([], 0, [], SearchMode.Similar, Semantic);
                case AnalyzeItemsCommand analyze:
                    Analyses.Add(analyze);
                    return JobQueued ? (TResponse)(object)Id<ProcessingJob>.New() : default!;
                default:
                    return await base.Send(request, cancellationToken);
            }
        }
    }
}
