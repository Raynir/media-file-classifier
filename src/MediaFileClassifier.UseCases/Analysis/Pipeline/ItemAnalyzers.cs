using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Tags;

namespace MediaFileClassifier.UseCases.Analysis.Pipeline;

/// <summary>
/// One analyzer of the pipeline (E06-S01-T03): it picks a provider, runs it on the item's prepared inputs and stores
/// the result. A provider failure is thrown; the stage records it.
/// </summary>
internal interface IItemAnalyzer
{
    /// <summary>
    /// Gets the analyzer key, see <see cref="AnalyzerKeys"/>.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Gets how large a still image must be decoded for this analyzer.
    /// </summary>
    InputSize InputSize { get; }

    /// <summary>
    /// Runs the analyzer on one item and stores its result.
    /// </summary>
    /// <param name="context">The item, its inputs and the analyzer's settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The analyzer's state, and whether it's stored already.</returns>
    Task<AnalyzerResult> RunAsync(AnalyzerContext context, CancellationToken cancellationToken);
}

/// <summary>
/// What an analyzer works on.
/// </summary>
/// <param name="Library">The open library.</param>
/// <param name="Item">The item.</param>
/// <param name="Inputs">The item's prepared inputs, shared by its analyzers.</param>
/// <param name="Settings">The analyzer's settings from the library's profile.</param>
internal sealed record AnalyzerContext(IOpenLibrary Library, MediaItem Item, AnalysisInputs Inputs, AnalyzerSettings Settings)
{
    /// <summary>
    /// Gets a name for the item's pictures, for provider logs.
    /// </summary>
    public string ImageName => Path.GetFileName(Item.Path.Value);
}

/// <summary>
/// How an analyzer ended.
/// </summary>
/// <param name="State">The analyzer's state.</param>
/// <param name="IsStored">Whether the analyzer stored the state with its result already.</param>
/// <param name="Failure">A failure for the work item, though the state is recorded, e.g. a video without frames.</param>
internal sealed record AnalyzerResult(AnalyzerState State, bool IsStored = false, Exception? Failure = null);

/// <summary>
/// Picks the provider an analyzer uses: the one its settings name, or the first one that runs on this platform.
/// </summary>
internal static class ProviderSelection
{
    /// <summary>
    /// Picks a provider.
    /// </summary>
    /// <typeparam name="TCapability">The capability.</typeparam>
    /// <param name="registry">The provider registry.</param>
    /// <param name="settings">The analyzer's settings.</param>
    /// <param name="breaker">The circuit breaker, or <c>null</c> to ignore it.</param>
    /// <param name="skipReason">Why there's none, see <see cref="AnalyzerReasons"/>.</param>
    /// <param name="paused">With <see cref="AnalyzerReasons.ProviderUnavailable"/>, the provider whose circuit is open.</param>
    /// <returns>The provider, or <c>null</c>.</returns>
    public static TCapability? Select<TCapability>(
        IAiProviderRegistry registry,
        AnalyzerSettings settings,
        IProviderCircuitBreaker? breaker,
        out string? skipReason,
        out TCapability? paused)
        where TCapability : class, IAiProvider
    {
        paused = null;
        // GetProviders hides providers that don't run here, so they're looked for among all of them.
        var candidates = registry.Providers.OfType<TCapability>()
            .Where(provider => settings.ProviderId is null || string.Equals(provider.Id, settings.ProviderId, StringComparison.Ordinal))
            .Where(provider => settings.ModelId is null || string.Equals(provider.Model.ModelName, settings.ModelId, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
        {
            skipReason = AnalyzerReasons.NoProvider;
            return null;
        }

        var supported = candidates.Where(provider => provider.IsSupportedOnCurrentPlatform).ToList();
        if (supported.Count == 0)
        {
            skipReason = AnalyzerReasons.UnsupportedPlatform;
            return null;
        }

        var available = supported.FirstOrDefault(provider => breaker?.IsOpen(provider.Id) != true);
        skipReason = available is null ? AnalyzerReasons.ProviderUnavailable : null;
        paused = available is null ? supported[0] : null;
        return available;
    }

    /// <summary>
    /// Creates the state of an analyzer without a provider. A provider paused by its circuit breaker also fails the
    /// work item with <see cref="AiFailureKind.Unreachable"/>, once the other results are stored, so the scheduler parks
    /// it until the circuit closes and then runs it again (E05-S04); the analyzers that finished are skipped then.
    /// </summary>
    /// <param name="context">The analyzer's context.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reason">Why, from <see cref="Select"/>.</param>
    /// <param name="paused">The paused provider, from <see cref="Select"/>.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The skipped state, and the failure that parks the work item, if any.</returns>
    public static AnalyzerResult Skip(AnalyzerContext context, string analyzer, string reason, IAiProvider? paused, DateTimeOffset now)
    {
        var result = Skip(context, analyzer, reason, now);
        if (paused is not { } provider)
        {
            return result;
        }

        var failure = new AiProviderException(
            AiFailureKind.Unreachable,
            provider.Id,
            $"{provider.DisplayName} is unavailable, so {analyzer} waits until it's back.");
        return result with { Failure = failure };
    }

    /// <summary>
    /// Creates the state of an analyzer without a provider.
    /// </summary>
    /// <param name="context">The analyzer's context.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="reason">Why, from <see cref="Select"/>.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The skipped state.</returns>
    public static AnalyzerResult Skip(AnalyzerContext context, string analyzer, string reason, DateTimeOffset now) =>
        new(AnalyzerState.Skipped(context.Item.Id, analyzer, reason, reason != AnalyzerReasons.UnsupportedPlatform, now));
}

/// <summary>
/// The SigLIP 2 embedding (E06-S03): a still image's vector, or a vector per keyframe and their mean.
/// </summary>
/// <param name="embedder">Embeds and stores the vectors.</param>
/// <param name="registry">The provider registry.</param>
/// <param name="clock">The clock.</param>
internal sealed class EmbeddingItemAnalyzer(ItemEmbeddingAnalyzer embedder, IAiProviderRegistry registry, TimeProvider clock) : IItemAnalyzer
{
    /// <inheritdoc />
    public string Key => AnalyzerKeys.Embedding;

    /// <inheritdoc />
    /// <remarks>A short side of at least 512 (D244); the provider resizes, so it isn't shrunk.</remarks>
    public InputSize InputSize { get; } = new(512, 0);

    /// <inheritdoc />
    public async Task<AnalyzerResult> RunAsync(AnalyzerContext context, CancellationToken cancellationToken)
    {
        // Embedding doesn't check the breaker: it's a local model.
        if (ProviderSelection.Select<IImageEmbeddingProvider>(registry, context.Settings, null, out var reason, out _) is not { } provider)
        {
            return ProviderSelection.Skip(context, Key, reason!, clock.GetUtcNow());
        }

        var itemId = context.Item.Id;
        if (context.Inputs.HasFrames)
        {
            var frames = await context.Inputs.GetFramesAsync(cancellationToken).ConfigureAwait(false);
            if (frames.Frames.Count == 0)
            {
                return NoFrames(context, frames.Warnings);
            }

            await embedder.EmbedKeyframesAsync(context.Library, itemId, frames.Frames, provider, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var still = await context.Inputs.GetStillAsync(null, cancellationToken).ConfigureAwait(false);
            await embedder.EmbedStillAsync(context.Library, itemId, still.ToAiImage(context.ImageName), provider, cancellationToken).ConfigureAwait(false);
        }

        return new AnalyzerResult(AnalyzerState.Done(itemId, Key, null, clock.GetUtcNow()));
    }

    // A video without frames can't be embedded. With FFmpeg missing the item waits (stays Pending) until it's
    // installed and the item is analyzed again; otherwise the stream can't be decoded, and the item fails like an
    // undecodable file.
    private AnalyzerResult NoFrames(AnalyzerContext context, IReadOnlyList<ExtractionWarning> warnings)
    {
        var warning = warnings.FirstOrDefault(warning => warning.Code == FrameSamplingWarnings.FrameSamplingFfmpegUnavailable)
            ?? warnings.FirstOrDefault()
            ?? new ExtractionWarning(FrameSamplingWarnings.FrameSamplingFailed);
        var state = AnalyzerState.Failed(context.Item.Id, Key, warning.Code, warning.Detail, null, isRetryable: true, clock.GetUtcNow());
        if (warning.Code == FrameSamplingWarnings.FrameSamplingFfmpegUnavailable)
        {
            return new AnalyzerResult(state);
        }

        var failure = new MediaFileException($"No frames could be read from the file: {warning}.") { IsPermanent = true };
        return new AnalyzerResult(state, Failure: failure);
    }
}

/// <summary>
/// The image describer (E06-S04): title, description, summary, tags and lists from a vision model. A video or animated
/// image gets one description of the whole clip (E06-S04-T07): a describer that takes several pictures gets up to
/// <see cref="MaxClipFrames"/> keyframes in one request; another describes up to <see cref="MaxDescribedFrames"/>
/// keyframes one by one, and the lists and tags are merged by frequency (D228), with the middle one's texts.
/// </summary>
/// <param name="registry">The provider registry.</param>
/// <param name="breaker">The circuit breaker, if any.</param>
/// <param name="clock">The clock.</param>
/// <param name="tagNormalizer">Normalises the tags (E06-S04-T05).</param>
internal sealed class DescriberItemAnalyzer(IAiProviderRegistry registry, IProviderCircuitBreaker? breaker, TimeProvider clock, TagNormalizer tagNormalizer)
    : IItemAnalyzer
{
    /// <summary>
    /// The longest edge of the picture sent to the describer.
    /// </summary>
    public const int MaxLongestEdge = 1024;

    /// <summary>
    /// The most keyframes sent in one request to a describer that takes several pictures.
    /// </summary>
    public const int MaxClipFrames = 6;

    /// <summary>
    /// The longest edge of keyframes sent together, so six of them fit a 4,096-token context with the answer.
    /// </summary>
    public const int ClipFrameLongestEdge = 512;

    /// <summary>
    /// The longest edge of a keyframe sent to a describer that takes one picture, as for a still.
    /// </summary>
    public const int FrameLongestEdge = 1024;

    /// <summary>
    /// The most items a merged list keeps, as one picture's description is limited to.
    /// </summary>
    public const int MaxMergedListItems = 30;

    /// <summary>
    /// The most keyframes a describer that takes one picture describes, one request each.
    /// </summary>
    public const int MaxDescribedFrames = 4;

    /// <inheritdoc />
    public string Key => AnalyzerKeys.Describer;

    /// <inheritdoc />
    public InputSize InputSize { get; } = new(0, MaxLongestEdge);

    /// <inheritdoc />
    public async Task<AnalyzerResult> RunAsync(AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (ProviderSelection.Select<IImageDescriber>(registry, context.Settings, breaker, out var reason, out var paused) is not { } provider)
        {
            return ProviderSelection.Skip(context, Key, reason!, paused, clock.GetUtcNow());
        }

        var options = new ImageDescriptionOptions(context.Settings.Language, PromptVersion: context.Settings.PromptVersion);
        DateTimeOffset startedAt;
        AiOutput<ImageDescription> output;
        if (context.Inputs.HasFrames)
        {
            var frames = await context.Inputs.GetFramesAsync(cancellationToken).ConfigureAwait(false);
            if (frames.Frames.Count == 0)
            {
                return ProviderSelection.Skip(context, Key, AnalyzerReasons.NoFrames, clock.GetUtcNow());
            }

            startedAt = clock.GetUtcNow();
            output = await DescribeClipAsync(provider, context.Inputs, [.. frames.Frames.Select(frame => frame.Image)], options, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var image = (await context.Inputs.GetStillAsync(MaxLongestEdge, cancellationToken).ConfigureAwait(false)).ToAiImage(context.ImageName);
            startedAt = clock.GetUtcNow();
            output = await provider.DescribeAsync(image, options, cancellationToken).ConfigureAwait(false);
        }

        breaker?.RecordSuccess(provider.Id);
        var now = clock.GetUtcNow();
        var model = await context.Library.Models.GetOrAddAsync(output.Model, now, cancellationToken).ConfigureAwait(false);
        var run = AnalysisRun.Start(context.Item.Id, Key, model, output.PromptVersion ?? provider.GetPromptVersion(context.Settings.PromptVersion), startedAt);
        run.Complete(now);

        // Only the declared fields are stored (E06-S04-T03): anything else the describer returned stays empty.
        var description = output.Value;
        var fields = provider.SupportedFields;
        var values = new List<AiFieldValue>();
        AddText(values, run, fields, DescriberFields.Title, DescriptiveField.Title, description.Title, now);
        AddText(values, run, fields, DescriberFields.Description, DescriptiveField.Description, description.Description, now);
        AddText(values, run, fields, DescriberFields.Summary, DescriptiveField.Summary, description.Summary, now);
        AddList(values, run, fields, DescriberFields.Objects, DescriptiveField.Objects, description.Objects, now);
        AddList(values, run, fields, DescriberFields.Scenes, DescriptiveField.Scenes, description.Scenes, now);
        AddList(values, run, fields, DescriberFields.Actions, DescriptiveField.Actions, description.Actions, now);
        AddList(values, run, fields, DescriberFields.Emotions, DescriptiveField.Emotions, description.Emotions, now);
        List<TagAssignment> tags = fields.HasFlag(DescriberFields.Tags)
            ? [.. tagNormalizer.Normalize(description.Tags)
                .Take(context.Settings.MaxTags ?? AnalysisProfile.DefaultMaxTags)
                .Select(tag => TagAssignment.Ai(run, tag, null, now))]
            : [];

        var state = AnalyzerState.Done(context.Item.Id, Key, run.Id, now);
        await context.Library.AnalyzerStates.ApplyRunAsync(run, values, tags, context.Library.Library.Settings.RetainedPreviousRuns, state, cancellationToken)
            .ConfigureAwait(false);
        return new AnalyzerResult(state, IsStored: true);
    }

    /// <summary>
    /// Picks up to <paramref name="count"/> frames spread evenly over the clip, the first and last included.
    /// </summary>
    /// <param name="frames">The keyframes, in time order.</param>
    /// <param name="count">The most to pick, at least 1.</param>
    /// <returns>The picked frames, in time order.</returns>
    internal static IReadOnlyList<AiImage> Spread(IReadOnlyList<AiImage> frames, int count)
    {
        if (frames.Count <= count)
        {
            return frames;
        }

        if (count == 1)
        {
            return [frames[frames.Count / 2]];
        }

        return [.. Enumerable.Range(0, count).Select(index => frames[(int)Math.Round(index * (frames.Count - 1) / (double)(count - 1))])];
    }

    private static void AddText(
        List<AiFieldValue> values,
        AnalysisRun run,
        DescriberFields supported,
        DescriberFields declaration,
        DescriptiveField field,
        string? text,
        DateTimeOffset now)
    {
        if (supported.HasFlag(declaration) && !string.IsNullOrWhiteSpace(text))
        {
            values.Add(AiFieldValue.For(run, field, FieldValue.FromText(text), null, now));
        }
    }

    private static void AddList(
        List<AiFieldValue> values,
        AnalysisRun run,
        DescriberFields supported,
        DescriberFields declaration,
        DescriptiveField field,
        IReadOnlyList<string>? items,
        DateTimeOffset now)
    {
        if (!supported.HasFlag(declaration))
        {
            return;
        }

        var value = FieldValue.FromList(items ?? []);
        if (value.Items.Count > 0)
        {
            values.Add(AiFieldValue.For(run, field, value, null, now));
        }
    }

    private async Task<AiOutput<ImageDescription>> DescribeClipAsync(
        IImageDescriber provider,
        AnalysisInputs inputs,
        IReadOnlyList<AiImage> frames,
        ImageDescriptionOptions options,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 1)
        {
            return await provider.DescribeAsync(frames[0], options, cancellationToken).ConfigureAwait(false);
        }

        if (provider is IMultiImageDescriber { MaxImages: >= 2 } multi)
        {
            var picked = Spread(frames, Math.Min(multi.MaxImages, MaxClipFrames)).Select(frame => inputs.ShrinkFrame(frame, ClipFrameLongestEdge)).ToList();
            var clip = options with
            {
                Context = $"The {picked.Count} pictures are frames of one video or animation, in time order. Describe the whole clip, not each frame.",
            };
            return await multi.DescribeFramesAsync(picked, clip, cancellationToken).ConfigureAwait(false);
        }

        var outputs = new List<AiOutput<ImageDescription>>();
        foreach (var frame in Spread(frames, MaxDescribedFrames))
        {
            outputs.Add(await provider.DescribeAsync(inputs.ShrinkFrame(frame, FrameLongestEdge), options, cancellationToken).ConfigureAwait(false));
        }

        // The texts come from the middle frame; tags are normalised per frame first, so "Dogs" and "dog" count as one.
        var middle = outputs[outputs.Count / 2];
        var descriptions = outputs.Select(output => output.Value).ToList();
        var merged = new ImageDescription(
            middle.Value.Title,
            middle.Value.Description,
            middle.Value.Summary,
            KeyframeAggregation.MergeLists([.. descriptions.Select<ImageDescription, IReadOnlyList<string>>(value => [.. tagNormalizer.Normalize(value.Tags).Select(tag => tag.Value)])]),
            [.. KeyframeAggregation.MergeLists([.. descriptions.Select(value => value.Objects)]).Take(MaxMergedListItems)],
            [.. KeyframeAggregation.MergeLists([.. descriptions.Select(value => value.Scenes)]).Take(MaxMergedListItems)],
            [.. KeyframeAggregation.MergeLists([.. descriptions.Select(value => value.Actions)]).Take(MaxMergedListItems)],
            [.. KeyframeAggregation.MergeLists([.. descriptions.Select(value => value.Emotions)]).Take(MaxMergedListItems)]);
        return middle with { Value = merged, Duration = outputs.Aggregate(TimeSpan.Zero, (sum, output) => sum + output.Duration) };
    }
}

/// <summary>
/// Text recognition (E06-S05). A video or animated image is read on every keyframe, and the distinct lines are kept.
/// </summary>
/// <param name="registry">The provider registry.</param>
/// <param name="breaker">The circuit breaker, if any.</param>
/// <param name="clock">The clock.</param>
internal sealed class OcrItemAnalyzer(IAiProviderRegistry registry, IProviderCircuitBreaker? breaker, TimeProvider clock) : IItemAnalyzer
{
    /// <summary>
    /// The longest edge of the picture sent to OCR.
    /// </summary>
    public const int MaxLongestEdge = 2048;

    /// <inheritdoc />
    public string Key => AnalyzerKeys.Ocr;

    /// <inheritdoc />
    public InputSize InputSize { get; } = new(0, MaxLongestEdge);

    /// <inheritdoc />
    public async Task<AnalyzerResult> RunAsync(AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (ProviderSelection.Select<IOcrProvider>(registry, context.Settings, breaker, out var reason, out var paused) is not { } provider)
        {
            return ProviderSelection.Skip(context, Key, reason!, paused, clock.GetUtcNow());
        }

        var startedAt = clock.GetUtcNow();
        ModelIdentity identity;
        string text;
        var gate = new DetectorGate(context.Settings?.PreFilterWithDetector == true && provider is not ITextDetector
            ? registry.Providers.OfType<ITextDetector>().FirstOrDefault()
            : null);
        if (context.Inputs.HasFrames)
        {
            var frames = await context.Inputs.GetFramesAsync(cancellationToken).ConfigureAwait(false);
            if (frames.Frames.Count == 0)
            {
                return ProviderSelection.Skip(context, Key, AnalyzerReasons.NoFrames, clock.GetUtcNow());
            }

            var texts = new List<string?>(frames.Frames.Count);
            identity = provider.Model;
            foreach (var frame in frames.Frames)
            {
                var output = await RecognizeAsync(provider, frame.Image, gate, cancellationToken).ConfigureAwait(false);
                if (output is null)
                {
                    texts.Add(null);
                    continue;
                }

                identity = output.Model;
                texts.Add(output.Value.HasText ? output.Value.Text : null);
            }

            text = string.Join('\n', KeyframeAggregation.MergeTextLines(texts));
            breaker?.RecordSuccess(provider.Id);
        }
        else
        {
            var still = await context.Inputs.GetStillAsync(MaxLongestEdge, cancellationToken).ConfigureAwait(false);
            var output = await RecognizeAsync(provider, still.ToAiImage(context.ImageName), gate, cancellationToken).ConfigureAwait(false);
            identity = output?.Model ?? provider.Model;
            text = output is { Value.HasText: true } ? output.Value.Text : string.Empty;
            breaker?.RecordSuccess(provider.Id);
        }

        var now = clock.GetUtcNow();
        var model = await context.Library.Models.GetOrAddAsync(identity, now, cancellationToken).ConfigureAwait(false);
        var run = AnalysisRun.Start(context.Item.Id, Key, model, null, startedAt);
        run.Complete(now);

        // A run without text is still applied: it replaces the text of an earlier run.
        List<AiFieldValue> values = string.IsNullOrWhiteSpace(text)
            ? []
            : [AiFieldValue.For(run, DescriptiveField.OcrText, FieldValue.FromText(text), null, now)];
        var state = AnalyzerState.Done(context.Item.Id, Key, run.Id, now);
        await context.Library.AnalyzerStates.ApplyRunAsync(run, values, [], context.Library.Library.Settings.RetainedPreviousRuns, state, cancellationToken)
            .ConfigureAwait(false);
        return new AnalyzerResult(state, IsStored: true);
    }

    /// <summary>
    /// Reads an image, unless the text detector finds no text in it. A detector that can't run, such as one whose model
    /// isn't installed, is dropped and the provider reads every image.
    /// </summary>
    private static async Task<AiOutput<OcrResult>?> RecognizeAsync(IOcrProvider provider, AiImage image, DetectorGate gate, CancellationToken cancellationToken)
    {
        if (gate.Detector is { } detector)
        {
            try
            {
                if (!await detector.HasTextAsync(image, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }
            }
            catch (AiProviderException)
            {
                gate.Detector = null;
            }
        }

        return await provider.RecognizeTextAsync(image, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The text detector an OCR run checks pictures with, until it fails.
/// </summary>
/// <param name="detector">The detector, or <see langword="null"/> to read every picture.</param>
internal sealed class DetectorGate(ITextDetector? detector)
{
    /// <summary>
    /// Gets or sets the detector, or <see langword="null"/> to read every picture.
    /// </summary>
    public ITextDetector? Detector { get; set; } = detector;
}
