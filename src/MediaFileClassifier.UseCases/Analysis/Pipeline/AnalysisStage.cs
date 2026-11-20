using System.Runtime.ExceptionServices;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Tags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Analysis.Pipeline;

/// <summary>
/// Analyzes one item (E06-S01): runs the enabled analyzers of the library's profile in order, sharing one decode or
/// sampling of the media, and stores each analyzer's result with its state in its own write.
/// </summary>
/// <remarks>
/// <para>
/// Partial success: a failing analyzer doesn't stop the others. Once every analyzer's result is stored, the item's status
/// is set (Indexed once its embedding is done, or the embedding is skipped), and then one held failure is rethrown so the
/// scheduler can retry it (E05-S04): the embedding's first, then a transient one, since that's the one a retry can fix.
/// A transient failure is recorded as <see cref="AnalyzerStatus.Retrying"/>, and becomes Failed only in
/// <see cref="OnFailedAsync"/>, when the scheduler gives up.
/// </para>
/// <para>
/// A retry, or a run after an interruption, skips the analyzers that already finished in the same work item. Since
/// each result is its own write, a cancelled item keeps the results of the analyzers that finished; running it again
/// replaces them.
/// </para>
/// </remarks>
public sealed partial class AnalysisStage : IModelDependentStage, IFailureAwareStage
{
    private const string AnalyzerKey = "MediaFileClassifier.Analyzer";

    private readonly IMediaFileSource files;
    private readonly IMediaFormatRegistry formats;
    private readonly IAnalysisImageDecoder decoder;
    private readonly IFrameSampler frameSampler;
    private readonly TimeProvider clock;
    private readonly ILogger<AnalysisStage> logger;
    private readonly IReadOnlyDictionary<string, IItemAnalyzer> analyzers;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisStage"/> class.
    /// </summary>
    /// <param name="files">Reads the item's file.</param>
    /// <param name="formats">Detects the file's format.</param>
    /// <param name="decoder">Decodes still images.</param>
    /// <param name="frameSampler">Samples keyframes.</param>
    /// <param name="embedder">Embeds and stores vectors.</param>
    /// <param name="registry">The AI provider registry.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="breaker">The provider circuit breaker (E05-S04), if one is registered.</param>
    /// <param name="tagNormalizer">Normalises the describer's tags (E06-S04-T05); the defaults if <c>null</c>.</param>
    public AnalysisStage(
        IMediaFileSource files,
        IMediaFormatRegistry formats,
        IAnalysisImageDecoder decoder,
        IFrameSampler frameSampler,
        ItemEmbeddingAnalyzer embedder,
        IAiProviderRegistry registry,
        TimeProvider clock,
        ILogger<AnalysisStage>? logger = null,
        IProviderCircuitBreaker? breaker = null,
        TagNormalizer? tagNormalizer = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(frameSampler);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);

        this.files = files;
        this.formats = formats;
        this.decoder = decoder;
        this.frameSampler = frameSampler;
        this.clock = clock;
        this.logger = logger ?? NullLogger<AnalysisStage>.Instance;
        IItemAnalyzer[] all =
        [
            new EmbeddingItemAnalyzer(embedder, registry, clock),
            new DescriberItemAnalyzer(registry, breaker, clock, tagNormalizer ?? new TagNormalizer()),
            new OcrItemAnalyzer(registry, breaker, clock),
        ];
        analyzers = all.ToDictionary(analyzer => analyzer.Key, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Stage => AnalysisStages.Item;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Ai;

    /// <inheritdoc />
    /// <remarks>The embedding model (D232): without it the stage waits instead of failing every item.</remarks>
    public ModelPurpose RequiredModel => ModelPurpose.Embedding;

    /// <summary>
    /// Gets the analyzer a failure came from, as tagged by the stage.
    /// </summary>
    /// <param name="exception">A failure thrown by <see cref="HandleAsync"/>.</param>
    /// <returns>The analyzer key, or <c>null</c> for a failure of the whole item, e.g. an unreadable file.</returns>
    public static string? GetAnalyzer(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Data[AnalyzerKey] is string analyzer)
            {
                return analyzer;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var itemId = context.WorkItem.ItemId ?? throw new InvalidOperationException($"A {Stage} work item needs an item.");
        var library = context.Library;
        var item = await library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || !CanAnalyze(item))
        {
            return;
        }

        var requested = GetRequested(library.Library.Settings.AnalysisProfile, AnalysisPayload.ReadAnalyzers(context.WorkItem.Payload));
        if (requested.Count == 0)
        {
            return;
        }

        AnalysisInputs? inputs = null;
        var previous = (await library.AnalyzerStates.GetForItemAsync(itemId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(state => state.Analyzer, StringComparer.Ordinal);
        var states = new Dictionary<string, AnalyzerState>(StringComparer.Ordinal);
        var failures = new List<(string Analyzer, Exception Exception, bool IsTransient)>();
        foreach (var settings in requested)
        {
            var key = settings.Analyzer;
            if (previous.TryGetValue(key, out var earlier) && IsFinished(earlier, context.WorkItem))
            {
                states[key] = earlier;
                continue;
            }

            if (!settings.Enabled)
            {
                states[key] = await SetAsync(library, AnalyzerState.Skipped(itemId, key, AnalyzerReasons.Disabled, false, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Prepared only when an analyzer runs, so a retry with nothing left to do doesn't touch the file. A file
            // that can't be opened fails the whole item, not one analyzer.
            inputs ??= await PrepareAsync(library, item, requested, cancellationToken).ConfigureAwait(false);

            try
            {
                var result = await analyzers[key].RunAsync(new AnalyzerContext(library, item, inputs, settings), cancellationToken).ConfigureAwait(false);
                states[key] = result.IsStored ? result.State : await SetAsync(library, result.State, cancellationToken).ConfigureAwait(false);
                if (result.Failure is { } failure)
                {
                    failures.Add((key, Tag(failure, key), IsTransient(failure)));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AiProviderException exception) when (exception.Kind == AiFailureKind.ModelMissing)
            {
                // The model isn't installed (D037): retryable once it is. Without the embedding the item waits.
                LogModelMissing(key, item.Path.Value, exception.Message);
                var state = AnalyzerState.Skipped(itemId, key, AnalyzerReasons.ModelMissing, true, clock.GetUtcNow(), exception.Message);
                states[key] = await SetAsync(library, state, cancellationToken).ConfigureAwait(false);
                if (key == AnalyzerKeys.Embedding)
                {
                    failures.Add((key, Tag(exception, key), false));
                }
            }
            catch (Exception exception)
            {
                var isTransient = IsTransient(exception);
                LogAnalyzerFailed(exception, key, item.Path.Value, isTransient);
                states[key] = await SetAsync(library, ToState(itemId, key, exception, isTransient), cancellationToken).ConfigureAwait(false);
                failures.Add((key, Tag(exception, key), isTransient));
            }
        }

        await UpdateItemStatusAsync(library, itemId, states, cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            var thrown = failures.FirstOrDefault(failure => failure.Analyzer == AnalyzerKeys.Embedding);
            if (thrown.Exception is null)
            {
                thrown = failures.FirstOrDefault(failure => failure.IsTransient);
            }

            if (thrown.Exception is null)
            {
                thrown = failures[0];
            }

            ExceptionDispatchInfo.Capture(thrown.Exception).Throw();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Turns the item's retrying analyzers into failures, retryable unless the error is permanent. The item goes to
    /// Error only when its embedding failed, or the whole item did (e.g. its file can't be read); a describer or OCR
    /// failure leaves it as it is.
    /// </remarks>
    public async Task OnFailedAsync(WorkItemContext context, ClassifiedError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        if (context.WorkItem.ItemId is not { } itemId || error.IsModelMissing)
        {
            return;
        }

        var library = context.Library;
        var item = await library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return;
        }

        var analyzer = GetAnalyzer(error.Exception);
        var isPermanent = error.Category == ErrorCategory.Permanent;
        var now = clock.GetUtcNow();
        var failed = (await library.AnalyzerStates.GetForItemAsync(itemId, cancellationToken).ConfigureAwait(false))
            .Where(state => state.Status == AnalyzerStatus.Retrying)
            .Select(state => state.ToFailed(!(isPermanent && state.Analyzer == analyzer), now))
            .ToList();
        await library.AnalyzerStates.SetAsync(failed, cancellationToken).ConfigureAwait(false);

        // A failure of the whole item (e.g. an unreadable file) counts only for a run that included the embedding: a
        // retry of OCR alone never changes the item's status.
        var requested = AnalysisPayload.ReadAnalyzers(context.WorkItem.Payload);
        var includesEmbedding = requested is null || requested.Contains(AnalyzerKeys.Embedding, StringComparer.Ordinal);
        if (analyzer is not (null or AnalyzerKeys.Embedding)
            || (analyzer is null && !includesEmbedding)
            || !MediaItemStatusTransitions.IsAllowed(item.Status, MediaItemStatus.Error))
        {
            return;
        }

        var stage = analyzer is null ? Stage : AnalysisStages.EmbeddingError;
        var exceptionType = error.Exception is MediaFileException ? null : error.Exception.GetType().Name;
        item.MarkFailed(new MediaItemError(stage, exceptionType, isPermanent), error.Message, now);
        await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decides whether a failure is passing, so the scheduler retries it and the analyzer shows as retrying. MFC-103's
    /// classifier has the final say: <see cref="OnFailedAsync"/> fixes up a retrying analyzer it doesn't retry.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><c>true</c> for an unreachable provider, a timeout, a server error or a locked file.</returns>
    internal static bool IsTransient(Exception exception) =>
        exception switch
        {
            AiProviderException ai => ai.Kind is AiFailureKind.Unreachable or AiFailureKind.Timeout or AiFailureKind.ProviderError,
            TimeoutException or HttpRequestException => true,
            FileNotFoundException or DirectoryNotFoundException => false,
            IOException => true,
            _ => false,
        };

    private static bool CanAnalyze(MediaItem item) =>
        item.Status is MediaItemStatus.Pending or MediaItemStatus.Indexed
        || (item.Status == MediaItemStatus.Error && item.Error is { } error && AnalysisStages.IsAnalysisStage(error.Stage));

    // The requested analyzers in profile order; analyzers that the profile or this version doesn't know are ignored.
    private List<AnalyzerSettings> GetRequested(AnalysisProfile profile, IReadOnlyList<string>? only) =>
        [.. profile.Analyzers.Where(settings => analyzers.ContainsKey(settings.Analyzer)
            && (only is null || only.Contains(settings.Analyzer, StringComparer.Ordinal)))];

    // Finished in this work item, so a retry of the work item doesn't run it again. A skip is checked again: a model
    // may have been installed, or a provider may be back.
    private static bool IsFinished(AnalyzerState state, WorkItem workItem) =>
        state.UpdatedAt >= workItem.CreatedAt
        && (state.Status is AnalyzerStatus.Done or AnalyzerStatus.NotApplicable || (state.Status == AnalyzerStatus.Failed && !state.IsRetryable));

    // One exception can fail several analyzers (a file that can't be decoded): the embedding's tag wins, since it
    // decides the item's status, then the first analyzer's.
    private static Exception Tag(Exception exception, string analyzer)
    {
        if (analyzer == AnalyzerKeys.Embedding || exception.Data[AnalyzerKey] is null)
        {
            exception.Data[AnalyzerKey] = analyzer;
        }

        return exception;
    }

    private static async Task<AnalyzerState> SetAsync(IOpenLibrary library, AnalyzerState state, CancellationToken cancellationToken)
    {
        await library.AnalyzerStates.SetAsync([state], cancellationToken).ConfigureAwait(false);
        return state;
    }

    // The item is Indexed once its embedding is done, or skipped for good (disabled, no provider, unsupported platform,
    // provider paused). A run without the embedding, e.g. a retry of OCR, leaves the status alone.
    private static bool IsIndexed(AnalyzerState embedding) =>
        embedding.Status is AnalyzerStatus.Done or AnalyzerStatus.NotApplicable
        || (embedding.Status == AnalyzerStatus.Skipped && embedding.ReasonCode != AnalyzerReasons.ModelMissing);

    private AnalyzerState ToState(Domain.Identity.Id<MediaItem> itemId, string analyzer, Exception exception, bool isTransient)
    {
        var now = clock.GetUtcNow();
        var reason = exception is AiProviderException ? AnalyzerReasons.ProviderFailed : AnalyzerReasons.InputFailed;
        var kind = (exception as AiProviderException)?.Kind.ToString();
        if (isTransient)
        {
            return AnalyzerState.Retrying(itemId, analyzer, reason, exception.Message, kind, now);
        }

        var isRetryable = exception is AiProviderException ai
            ? ai.Kind is not (AiFailureKind.InvalidInput or AiFailureKind.InvalidOutput)
            : !ItemStage.IsPermanent(exception);
        return AnalyzerState.Failed(itemId, analyzer, reason, exception.Message, kind, isRetryable, now);
    }

    private async Task<AnalysisInputs> PrepareAsync(IOpenLibrary library, MediaItem item, List<AnalyzerSettings> requested, CancellationToken cancellationToken)
    {
        var folder = await library.Folders.GetAsync(item.FolderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The item's folder isn't in the library.");
        OfflineFolders.ThrowIfOffline(folder);
        var location = ItemStage.GetLocation(folder, item);
        MediaFormatDetection format;
        Stream stream;
        try
        {
            stream = await files.OpenReadAsync(location, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OfflineFolders.IsMissingDirectory(exception))
        {
            // D188, D025: an Offline folder parks the work; a deleted subfolder fails it like a deleted file.
            await OfflineFolders.ThrowForMissingDirectoryAsync(library, folder, location, files, clock, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await using (stream.ConfigureAwait(false))
        {
            format = await formats.DetectAsync(stream, item.Path.Value, cancellationToken).ConfigureAwait(false);
        }

        if (!format.IsSupported)
        {
            throw new MediaFileException($"The file isn't a supported media file: {format.Rejection}.");
        }

        var metadata = await library.Items.GetTechnicalMetadataAsync(item.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The item has no technical metadata.");
        var content = new MediaContent(item.Id, item.FileModifiedAt, token => files.OpenReadAsync(location, token))
        {
            LocalPath = ItemStage.GetLocalPath(folder, item),
        };
        var size = requested.Where(settings => settings.Enabled)
            .Aggregate(new InputSize(0, 0), (union, settings) => union.Union(analyzers[settings.Analyzer].InputSize));
        return new AnalysisInputs(decoder, frameSampler, library, content, format, metadata, size);
    }

    private async Task UpdateItemStatusAsync(
        IOpenLibrary library,
        Domain.Identity.Id<MediaItem> itemId,
        Dictionary<string, AnalyzerState> states,
        CancellationToken cancellationToken)
    {
        if (!states.TryGetValue(AnalyzerKeys.Embedding, out var embedding) || !IsIndexed(embedding))
        {
            return;
        }

        // Read again: the item may have changed while the analyzers ran, e.g. its file went missing.
        var item = await library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Status == MediaItemStatus.Indexed || !CanAnalyze(item))
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (item.Status == MediaItemStatus.Error)
        {
            item.ChangeStatus(MediaItemStatus.Pending, now);
        }

        item.ChangeStatus(MediaItemStatus.Indexed, now);
        await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {Analyzer} failed for {Path} (transient: {IsTransient})")]
    private partial void LogAnalyzerFailed(Exception exception, string analyzer, string path, bool isTransient);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer {Analyzer} skipped {Path}: its model isn't installed ({Message})")]
    private partial void LogModelMissing(string analyzer, string path, string message);
}
