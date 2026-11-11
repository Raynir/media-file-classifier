using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Search.FindSimilar;

/// <summary>
/// Where "Analyze now" (E09-S03-T03) stands for the item of a "find similar" search.
/// </summary>
public enum AnalyzeNowState
{
    /// <summary>
    /// Nothing to offer: not in similarity mode, or the item has an embedding.
    /// </summary>
    Hidden,

    /// <summary>
    /// The item has no embedding: "Analyze now" is offered.
    /// </summary>
    Offered,

    /// <summary>
    /// The item is queued or being analyzed; the search runs again once its embedding lands.
    /// </summary>
    Analyzing,

    /// <summary>
    /// The item is queued, but background processing is paused until the user resumes it.
    /// </summary>
    Paused,

    /// <summary>
    /// Background processing isn't running, so nothing was queued.
    /// </summary>
    NotRunning,

    /// <summary>
    /// The analysis ran but produced no embedding: the embedding failed or was skipped (e.g. D037).
    /// </summary>
    Failed,
}

/// <summary>
/// "Analyze now" for a "find similar" search on an item without an embedding (E09-S03-T03, D036): queues the item's
/// whole analysis profile at <see cref="JobPriority.UserAction"/>, and runs the search again once the item's embedding
/// is committed, so the results appear without further action. When the embedding fails or is skipped instead, the
/// state says so rather than waiting forever.
/// </summary>
/// <remarks>
/// The search index applies committed embeddings in its own <see cref="IEmbeddingRepository.Changed"/> handler, which
/// it adds when the library opens. This class adds its handler only when the user asks, so the index already has the
/// vector when the search runs again.
/// </remarks>
public sealed partial class AnalyzeNow
{
    private readonly SearchResultSource source;
    private readonly ISender sender;
    private readonly ILibrarySession session;
    private readonly IJobSystem? jobs;
    private readonly TimeProvider clock;
    private readonly ILogger<AnalyzeNow> logger;
    private readonly Lock gate = new();
    private AnalyzeNowState state;
    private Watch? watch;

    // The item that Failed or NotRunning is about; the state stays while the search is of that item.
    private Id<MediaItem>? outcomeItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzeNow"/> class.
    /// </summary>
    /// <param name="source">The search results, in similarity mode.</param>
    /// <param name="sender">Sends <see cref="AnalyzeItemsCommand"/>.</param>
    /// <param name="session">The open library, whose changes tell when the analysis is done.</param>
    /// <param name="jobs">Tells whether processing is paused, or <see langword="null"/> to not tell.</param>
    /// <param name="clock">The clock, or <see langword="null"/> for the system clock.</param>
    /// <param name="logger">Logger.</param>
    public AnalyzeNow(
        SearchResultSource source,
        ISender sender,
        ILibrarySession session,
        IJobSystem? jobs = null,
        TimeProvider? clock = null,
        ILogger<AnalyzeNow>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(session);

        this.source = source;
        this.sender = sender;
        this.session = session;
        this.jobs = jobs;
        this.clock = clock ?? TimeProvider.System;
        this.logger = logger ?? NullLogger<AnalyzeNow>.Instance;
        source.SummaryChanged += (_, _) => Update();
        source.Changed += (_, _) => Update();
        session.CurrentChanged += (_, _) => Update();
    }

    /// <summary>
    /// Raised when <see cref="State"/> changes, on any thread.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets where "Analyze now" stands.
    /// </summary>
    public AnalyzeNowState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <summary>
    /// Queues the item of the "find similar" search for analysis, unless it's already queued.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the item is queued.</returns>
    public async Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        Watch started;
        lock (gate)
        {
            if (source.SimilarTo is not { } similar || watch is not null || session.Current is not { } library)
            {
                return;
            }

            started = new Watch(this, library, similar.ItemId, clock.GetUtcNow());
            watch = started;
            outcomeItem = null;
        }

        SetState(AnalyzeNowState.Analyzing);
        Id<ProcessingJob>? jobId;
        try
        {
            jobId = await sender.Send(
                new AnalyzeItemsCommand([started.ItemId], Analyzers: null, Description: null, Priority: JobPriority.UserAction),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Stop(started, AnalyzeNowState.Offered);
            throw;
        }

        if (jobId is null)
        {
            Stop(started, AnalyzeNowState.NotRunning);
        }
        else if (jobs?.Current?.IsPaused == true)
        {
            SetState(AnalyzeNowState.Paused, started);
        }
    }

    // Follows the search: leaving similarity mode, another item or library stops watching; a search that found the
    // item analyzed hides the offer, and one that didn't offers it, unless the item is being analyzed or its outcome
    // is shown.
    private void Update()
    {
        var similar = source.SimilarTo;
        var summary = source.Summary;
        bool changed;
        lock (gate)
        {
            if (watch is { } current && (similar?.ItemId != current.ItemId || !ReferenceEquals(session.Current, current.Library)))
            {
                current.Detach();
                watch = null;
            }

            if (outcomeItem is { } item && similar?.ItemId != item)
            {
                outcomeItem = null;
            }

            var keep = watch is not null || outcomeItem is not null;
            AnalyzeNowState next;
            if (similar is null)
            {
                next = AnalyzeNowState.Hidden;
            }
            else if (summary?.SimilarTo?.ItemId != similar.ItemId)
            {
                // The summary is of an older search; this one's comes later.
                next = keep ? state : AnalyzeNowState.Hidden;
            }
            else if (summary.Semantic != SemanticSearchStatus.NotAnalyzed)
            {
                watch?.Detach();
                watch = null;
                outcomeItem = null;
                next = AnalyzeNowState.Hidden;
            }
            else
            {
                next = keep ? state : AnalyzeNowState.Offered;
            }

            changed = state != next;
            state = next;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetState(AnalyzeNowState next, Watch? onlyFor = null)
    {
        lock (gate)
        {
            if (state == next || (onlyFor is not null && !ReferenceEquals(watch, onlyFor)))
            {
                return;
            }

            state = next;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Stop(Watch stopped, AnalyzeNowState next)
    {
        lock (gate)
        {
            if (!ReferenceEquals(watch, stopped))
            {
                return;
            }

            stopped.Detach();
            watch = null;
            outcomeItem = next is AnalyzeNowState.Failed or AnalyzeNowState.NotRunning ? stopped.ItemId : null;
        }

        SetState(next);
    }

    private void OnEmbeddingsCommitted(Watch committed)
    {
        lock (gate)
        {
            if (!ReferenceEquals(watch, committed))
            {
                return;
            }
        }

        // The index already applied the vector (see the remarks), so the search finds it now.
        source.RefreshSimilar();
    }

    private async Task CheckAnalyzerStateAsync(Watch checking)
    {
        AnalyzerState? embedding;
        try
        {
            var states = await checking.Library.AnalyzerStates.GetForItemAsync(checking.ItemId).ConfigureAwait(false);
            embedding = states.FirstOrDefault(analyzerState => analyzerState.Analyzer == AnalyzerKeys.Embedding);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // E.g. the library closed meanwhile; the next change checks again.
            LogCheckFailed(exception);
            return;
        }

        // A state from before the click is the outcome of an earlier run, not of this one.
        if (embedding is null || embedding.UpdatedAt < checking.QueuedAt)
        {
            return;
        }

        switch (embedding.Status)
        {
            case AnalyzerStatus.Failed or AnalyzerStatus.Skipped or AnalyzerStatus.NotApplicable:
                Stop(checking, AnalyzeNowState.Failed);
                break;
            case AnalyzerStatus.Done:
                // Done without a vector the search can use: show the outcome of the search instead of waiting.
                Stop(checking, AnalyzeNowState.Hidden);
                source.RefreshSimilar();
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Couldn't check the analysis of the item to find similar items to")]
    private partial void LogCheckFailed(Exception exception);

    // The library events of one "Analyze now" click.
    private sealed class Watch
    {
        private readonly AnalyzeNow owner;
        private int pendingChecks;

        public Watch(AnalyzeNow owner, IOpenLibrary library, Id<MediaItem> itemId, DateTimeOffset queuedAt)
        {
            this.owner = owner;
            Library = library;
            ItemId = itemId;
            QueuedAt = queuedAt;
            library.Embeddings.Changed += OnEmbeddingsChanged;
            library.Items.Changed += OnItemsChanged;
        }

        public IOpenLibrary Library { get; }

        public Id<MediaItem> ItemId { get; }

        public DateTimeOffset QueuedAt { get; }

        public void Detach()
        {
            Library.Embeddings.Changed -= OnEmbeddingsChanged;
            Library.Items.Changed -= OnItemsChanged;
        }

        private void OnEmbeddingsChanged(object? sender, EmbeddingsChangedEventArgs e)
        {
            if (e.Changes.Any(change => change.Embeddings.ItemId == ItemId && change.Embeddings.Embeddings.Count > 0))
            {
                owner.OnEmbeddingsCommitted(this);
            }
        }

        // The analysis sets the item's status once every analyzer's result is stored. Item changes come in bursts
        // while importing, so one check runs at a time; changes meanwhile make it check once more when it's done.
        private void OnItemsChanged(object? sender, EventArgs e)
        {
            if (Interlocked.Increment(ref pendingChecks) == 1)
            {
                _ = CheckAsync();
            }
        }

        private async Task CheckAsync()
        {
            do
            {
                Volatile.Write(ref pendingChecks, 1);
                await owner.CheckAnalyzerStateAsync(this).ConfigureAwait(false);
            }
            while (Interlocked.CompareExchange(ref pendingChecks, 0, 1) != 1);
        }
    }
}
