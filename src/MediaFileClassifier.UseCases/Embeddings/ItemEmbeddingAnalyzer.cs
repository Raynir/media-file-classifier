using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// Computes and stores an item's semantic embeddings (E06-S03-T01). A still image gets one vector. A video or
/// animated image gets one vector per keyframe (E06-S02) and the mean of the normalised keyframe vectors as its item
/// vector (D228). Vectors are stored through <see cref="IOpenLibrary.Embeddings"/>, the single write path that
/// keeps the search index current (D218).
/// </summary>
/// <remarks>
/// The analysis pipeline (E06-S01) calls it per item. Don't call it from inside another library write (D072).
/// </remarks>
public sealed partial class ItemEmbeddingAnalyzer
{
    private readonly IAiProviderRegistry registry;
    private readonly IFrameSampler frameSampler;
    private readonly IModelStore modelStore;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly ConcurrentDictionary<(string Library, string Model), bool> recordedUsage = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemEmbeddingAnalyzer"/> class.
    /// </summary>
    /// <param name="registry">AI providers.</param>
    /// <param name="frameSampler">Keyframe sampler for videos and animated images.</param>
    /// <param name="modelStore">Model store, told which libraries use a local model.</param>
    /// <param name="clock">Clock.</param>
    /// <param name="logger">Logger.</param>
    public ItemEmbeddingAnalyzer(
        IAiProviderRegistry registry,
        IFrameSampler frameSampler,
        IModelStore modelStore,
        TimeProvider clock,
        ILogger<ItemEmbeddingAnalyzer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(frameSampler);
        ArgumentNullException.ThrowIfNull(modelStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.registry = registry;
        this.frameSampler = frameSampler;
        this.modelStore = modelStore;
        this.clock = clock;
        this.logger = logger ?? NullLogger<ItemEmbeddingAnalyzer>.Instance;
    }

    /// <summary>
    /// Embeds one item: reads a still image, or samples and stores the keyframes of a video or animated image.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="content">The item's content.</param>
    /// <param name="format">The detected format.</param>
    /// <param name="metadata">The item's technical metadata (duration, orientation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was stored. With no frames (a sampling warning), nothing is stored.</returns>
    /// <exception cref="AiProviderException">No embedding provider is available, or it failed. <see cref="AiFailureKind.ModelMissing"/> means wait (D037).</exception>
    public async Task<ItemEmbeddingResult> AnalyzeAsync(
        IOpenLibrary library,
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metadata);

        // Fail before sampling when there's nothing to embed with.
        _ = GetProvider();
        if (format.MediaType is MediaType.Video or MediaType.AnimatedImage)
        {
            var sample = await frameSampler.SampleAsync(content, format, metadata, FrameSamplingOptions.Default, cancellationToken).ConfigureAwait(false);
            if (sample.Frames.Count == 0)
            {
                return new ItemEmbeddingResult(null, 0, sample.Warnings);
            }

            var keyframes = sample.Frames.Select(frame => frame.ToKeyframe(content.ItemId)).ToList();
            await library.Keyframes.ReplaceAsync(content.ItemId, keyframes, cancellationToken).ConfigureAwait(false);
            var frames = keyframes
                .Zip(sample.Frames, (keyframe, frame) => new KeyframeImage(keyframe, AiImage.FromRgba(frame.Pixels, frame.Width, frame.Height, $"frame {frame.Index}")))
                .ToList();
            var stored = await EmbedKeyframesAsync(library, content.ItemId, frames, cancellationToken).ConfigureAwait(false);
            return stored with { Warnings = sample.Warnings };
        }

        var bytes = await ReadAsync(content, cancellationToken).ConfigureAwait(false);
        return await EmbedStillAsync(library, content.ItemId, new AiImage(bytes, "image/*", Path.GetFileName(content.LocalPath)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Embeds a still image and stores its vector as the item vector, replacing the item's vectors from that model.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="image">The picture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was stored.</returns>
    public async Task<ItemEmbeddingResult> EmbedStillAsync(IOpenLibrary library, Id<MediaItem> itemId, AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(image);

        var output = await GetProvider().EmbedImagesAsync([image], cancellationToken).ConfigureAwait(false);
        var model = await RegisterAsync(library, output.Model, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        Embedding[] embeddings = [Embedding.Create(itemId, model, EmbeddingKind.Image, output.Value[0].Span, now)];
        await library.Embeddings.ReplaceAsync(itemId, model.Id, embeddings, cancellationToken).ConfigureAwait(false);
        return new ItemEmbeddingResult(model, 0, []);
    }

    /// <summary>
    /// Embeds stored keyframes and stores one vector per keyframe plus their mean as the item vector (D228),
    /// replacing the item's vectors from that model.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="frames">The keyframes, already stored with <c>IKeyframeRepository.ReplaceAsync</c>, and their pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was stored.</returns>
    public async Task<ItemEmbeddingResult> EmbedKeyframesAsync(
        IOpenLibrary library,
        Id<MediaItem> itemId,
        IReadOnlyList<KeyframeImage> frames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("There are no keyframes to embed.", nameof(frames));
        }

        var output = await GetProvider().EmbedImagesAsync([.. frames.Select(frame => frame.Image)], cancellationToken).ConfigureAwait(false);
        var model = await RegisterAsync(library, output.Model, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var embeddings = new List<Embedding>(frames.Count + 1)
        {
            Embedding.Create(itemId, model, EmbeddingKind.Image, KeyframeAggregation.MeanEmbedding(output.Value), now),
        };
        for (var i = 0; i < frames.Count; i++)
        {
            embeddings.Add(Embedding.Create(itemId, model, EmbeddingKind.Image, output.Value[i].Span, now, frames[i].Keyframe.Id));
        }

        await library.Embeddings.ReplaceAsync(itemId, model.Id, embeddings, cancellationToken).ConfigureAwait(false);
        return new ItemEmbeddingResult(model, frames.Count, []);
    }

    internal static async Task<byte[]> ReadAsync(MediaContent content, CancellationToken cancellationToken)
    {
        if (content.LocalPath is { } path)
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }

    private IImageEmbeddingProvider GetProvider() =>
        registry.GetProviders<IImageEmbeddingProvider>().FirstOrDefault()
            ?? throw new AiProviderException(AiFailureKind.ModelMissing, "embeddings", "No image embedding provider is registered.");

    private async Task<ModelRegistration> RegisterAsync(IOpenLibrary library, ModelIdentity identity, CancellationToken cancellationToken)
    {
        var model = await library.Models.GetOrAddAsync(identity, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var root = library.Layout.RootDirectory;
        if (identity.ProviderId == ProviderIds.Onnx && identity.Version is { } version && identity.FileHash is { } contentHash
            && recordedUsage.TryAdd((root, identity.ToString() + "#" + contentHash), true))
        {
            try
            {
                await modelStore.RecordUsageAsync(identity.ModelName, version, contentHash, root, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ModelStoreException)
            {
                // Only the delete warning (E06-S12) depends on it; try again next time.
                recordedUsage.TryRemove((root, identity.ToString() + "#" + contentHash), out _);
                LogUsageNotRecorded(ex, identity.ToString());
            }
        }

        return model;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model store couldn't record that this library uses {Model}")]
    private partial void LogUsageNotRecorded(Exception exception, string model);
}
