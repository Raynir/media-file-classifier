using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// Switches search to a new embedding model (E06-S07-T05). While items are re-indexed, the new model's vectors are
/// stored beside the old ones and search keeps using the active search model
/// (<see cref="LibrarySettings.ActiveSearchModelId"/>). Once the share of the items with old vectors that have new
/// ones too reaches <see cref="LibrarySettings.EmbeddingSwitchThreshold"/>, or at once when the old model's provider is
/// gone, the current model becomes the active one and the old vectors of the items that have new ones are removed.
/// The rest keep theirs: they're Outdated, and semantic search skips them until they're re-indexed (D051).
/// </summary>
/// <remarks>
/// A singleton; resolve it at startup. It checks when a library opens, and <see cref="Delay"/> after new vectors of
/// another model than the active one were written, so a re-index checks at most once per delay.
/// </remarks>
[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "A background check may still wait on the semaphore or read the token after disposal; neither holds a timer or handle that needs disposing.")]
public sealed partial class EmbeddingModelMigration : IDisposable
{
    /// <summary>
    /// How long after a write of new-model vectors the check runs, collecting the writes in between.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private readonly IAiProviderRegistry registry;
    private readonly ILibrarySession session;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly TimeSpan delay;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim checking = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private IOpenLibrary? attached;
    private ITimer? timer;
    private Task pending = Task.CompletedTask;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddingModelMigration"/> class, and checks the open library.
    /// </summary>
    /// <param name="registry">AI providers.</param>
    /// <param name="session">The open library.</param>
    /// <param name="clock">The clock the delay runs on.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="delay">The delay after a write, or <see langword="null"/> for <see cref="Delay"/>.</param>
    public EmbeddingModelMigration(
        IAiProviderRegistry registry,
        ILibrarySession session,
        TimeProvider clock,
        ILogger<EmbeddingModelMigration>? logger = null,
        TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);

        this.registry = registry;
        this.session = session;
        this.clock = clock;
        this.logger = logger ?? NullLogger<EmbeddingModelMigration>.Instance;
        this.delay = delay ?? Delay;
        session.CurrentChanged += OnCurrentChanged;
        Attach(session.Current);
    }

    /// <summary>
    /// Gets the check that ran or runs last in the background. It never throws.
    /// </summary>
    public Task Pending
    {
        get
        {
            lock (gate)
            {
                return pending;
            }
        }
    }

    /// <summary>
    /// Checks whether the switch rule holds.
    /// </summary>
    /// <param name="progress">How far the migration got.</param>
    /// <param name="threshold">The library's <see cref="LibrarySettings.EmbeddingSwitchThreshold"/>.</param>
    /// <returns><see langword="true"/> when search should switch to the new model.</returns>
    public static bool IsReady(EmbeddingMigrationProgress progress, double threshold) => progress.Share >= threshold;

    /// <summary>
    /// Checks the open library now: sets its active search model when it has none, and switches it to the current
    /// embedding model when the rule holds. Checks run one at a time.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the active search model changed to the current model.</returns>
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return false;
        }

        await checking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CheckAsync(library, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            checking.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.CurrentChanged -= OnCurrentChanged;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Detach();
        }

        stopping.Cancel();
    }

    private static Id<ModelRegistration>? Newest(IEnumerable<ModelRegistration> models) =>
        models.OrderBy(model => model.RegisteredAt).Select(model => (Id<ModelRegistration>?)model.Id).LastOrDefault();

    // Checks are serialised, so this needs no lock.
    private IOpenLibrary? cleanedUp;

    private async Task<bool> CheckAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        var settings = library.Library.Settings;
        var current = SearchModels.FindCurrentEmbedder(registry, settings);
        var models = await library.Models.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var inUse = (await library.Embeddings.GetModelsInUseAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var currentIds = current is null ? [] : models.Where(model => SearchModels.Matches(current.Model, model)).Select(model => model.Id).ToList();

        // The current model's registration that has vectors; without one there's nothing to switch to yet.
        var target = Newest(models.Where(model => currentIds.Contains(model.Id) && inUse.Contains(model.Id)));
        if (settings.ActiveSearchModelId is not { } active)
        {
            if (inUse.Count == 1)
            {
                // An existing library: search used its only model's vectors all along.
                await SetActiveAsync(library, inUse.Single(), cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (target is not { } initial)
            {
                return false;
            }

            LogInitialised(library.Library.Name);
            await SwitchAsync(library, initial, currentIds, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (currentIds.Contains(active))
        {
            // A switch interrupted after search moved leaves old vectors behind: finish it once per opened library.
            if (!ReferenceEquals(cleanedUp, library))
            {
                var left = await library.Embeddings.RemoveSupersededAsync(currentIds, cancellationToken).ConfigureAwait(false);
                cleanedUp = library;
                LogCleanedUp(left);
            }

            return false;
        }

        if (target is not { } next)
        {
            return false;
        }

        var old = models.FirstOrDefault(model => model.Id == active);
        var installed = old is not null && registry.GetProviders<ITextEmbeddingProvider>().Any(provider => SearchModels.Matches(provider.Model, old));
        if (installed)
        {
            var progress = await library.Embeddings.GetMigrationProgressAsync(active, currentIds, cancellationToken).ConfigureAwait(false);
            if (!IsReady(progress, settings.EmbeddingSwitchThreshold))
            {
                return false;
            }

            LogSwitching(library.Library.Name, progress.ItemsWithBoth, progress.ItemsWithOldModel);
        }
        else
        {
            LogOldModelGone(library.Library.Name);
        }

        await SwitchAsync(library, next, currentIds, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Search moves first, so the old vectors are no longer needed when they're removed.
    private async Task SwitchAsync(IOpenLibrary library, Id<ModelRegistration> target, IReadOnlyCollection<Id<ModelRegistration>> currentIds, CancellationToken cancellationToken)
    {
        await SetActiveAsync(library, target, cancellationToken).ConfigureAwait(false);
        var cleaned = await library.Embeddings.RemoveSupersededAsync(currentIds, cancellationToken).ConfigureAwait(false);
        LogCleanedUp(cleaned);
    }

    private async Task SetActiveAsync(IOpenLibrary library, Id<ModelRegistration> modelId, CancellationToken cancellationToken)
    {
        var entity = library.Library;
        var previous = entity.Settings;
        entity.ChangeSettings(previous with { ActiveSearchModelId = modelId }, clock.GetUtcNow());
        try
        {
            await library.UpdateLibraryAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Undo only this change: a profile save may have changed other settings meanwhile.
            entity.ChangeSettings(entity.Settings with { ActiveSearchModelId = previous.ActiveSearchModelId }, clock.GetUtcNow());
            throw;
        }
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => Attach(session.Current);

    private void Attach(IOpenLibrary? library)
    {
        lock (gate)
        {
            if (disposed || ReferenceEquals(attached, library))
            {
                return;
            }

            Detach();
            attached = library;
            if (library is null)
            {
                return;
            }

            library.Embeddings.Changed += OnEmbeddingsChanged;
            StartLocked();
        }
    }

    private void Detach()
    {
        timer?.Dispose();
        timer = null;
        if (attached is { } library)
        {
            library.Embeddings.Changed -= OnEmbeddingsChanged;
            attached = null;
        }
    }

    // Raised on the writer: only schedule the check, never read or write here.
    private void OnEmbeddingsChanged(object? sender, EmbeddingsChangedEventArgs e)
    {
        lock (gate)
        {
            if (disposed || attached is not { } library)
            {
                return;
            }

            var active = library.Library.Settings.ActiveSearchModelId;
            if (timer is null && e.Changes.Any(change => change.Embeddings.Embeddings.Count > 0 && change.Embeddings.ModelId != active))
            {
                // The handler runs inside the write; the check must not inherit its context, or its own writes would
                // look nested in it (D072).
                using (ExecutionContext.SuppressFlow())
                {
                    timer = clock.CreateTimer(_ => Start(), null, delay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void Start()
    {
        lock (gate)
        {
            if (!disposed)
            {
                StartLocked();
            }
        }
    }

    private void StartLocked()
    {
        timer?.Dispose();
        timer = null;
        var previous = pending;
        using (ExecutionContext.SuppressFlow())
        {
            pending = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await RunAsync().ConfigureAwait(false);
            });
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await CheckAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The library may have closed meanwhile; the next opening or write checks again.
            LogCheckFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Library {Library} had no active search model; search uses the current embedding model")]
    private partial void LogInitialised(string library);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library {Library}: {Both} of {Old} items have vectors of the new embedding model, so search switches to it")]
    private partial void LogSwitching(string library, int both, int old);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library {Library}: the active search model is no longer installed, so search switches to the current embedding model")]
    private partial void LogOldModelGone(string library);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the old embedding vectors of {Items} items")]
    private partial void LogCleanedUp(int items);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking the embedding model migration failed")]
    private partial void LogCheckFailed(Exception exception);
}
