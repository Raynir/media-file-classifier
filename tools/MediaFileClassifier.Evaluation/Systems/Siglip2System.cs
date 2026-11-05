using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// Runs the app's own SigLIP 2 pipeline over the corpus (E06-S03-T05): the same provider, image preprocessing,
/// tokenizer and keyframe sampler the app uses. A photo, screenshot or document is one vector, and a GIF or video is
/// the mean of its keyframes' vectors (D228). Queries rank items by cosine similarity.
/// </summary>
/// <remarks>
/// Videos need FFmpeg on the machine. Without it, a video is skipped and the run says so.
/// </remarks>
public sealed class Siglip2System : IRetrievalSystem, IDisposable
{
    private readonly Siglip2EmbeddingProvider provider;
    private readonly ServiceProvider services;
    private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly Action<string> log;

    /// <summary>
    /// Initializes a new instance of the <see cref="Siglip2System"/> class.
    /// </summary>
    /// <param name="modelDirectory">The folder with <c>vision_model.onnx</c>, <c>text_model.onnx</c> and <c>tokenizer.model</c>.</param>
    /// <param name="log">Where progress and warnings go, one line per call.</param>
    public Siglip2System(string modelDirectory, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentNullException.ThrowIfNull(log);

        this.log = log;
        provider = new Siglip2EmbeddingProvider(new DirectoryModelStore(Path.GetFullPath(modelDirectory)), new OnnxSessionFactory(), new Siglip2Options());
        var capabilities = new CapabilityService([], NullLogger<CapabilityService>.Instance);
        capabilities.SetAvailable(KnownCapabilities.VideoAnalysis);
        services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ICapabilityService>(capabilities)
            .AddSingleton<IPerceptualHasher, SkiaPerceptualHasher>()
            .AddMedia()
            .BuildServiceProvider();
    }

    /// <inheritdoc />
    public string Name => "siglip2-base-patch16-256-app";

    /// <inheritdoc />
    public async Task IndexAsync(IReadOnlyList<CorpusItem> items, string mediaRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        vectors.Clear();
        var skipped = 0;
        var done = 0;
        foreach (var item in items)
        {
            var vector = await EmbedItemAsync(Path.Combine(mediaRoot, item.File), cancellationToken).ConfigureAwait(false);
            if (vector is null)
            {
                skipped++;
                log($"  skipped {item.Id}: no frames (is FFmpeg installed?)");
            }
            else
            {
                vectors[item.Id] = vector;
            }

            if (++done % 50 == 0)
            {
                log($"  embedded {done} of {items.Count} items");
            }
        }

        if (skipped > 0)
        {
            log($"Warning: {skipped} items have no vector, so the numbers are not comparable with the baseline.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SearchAsync(EvalQuery query, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var output = await provider.EmbedTextsAsync([query.Text], cancellationToken).ConfigureAwait(false);
        var text = output.Value[0].ToArray();
        return
        [
            .. vectors
                .Select(pair => (pair.Key, Score: System.Numerics.Tensors.TensorPrimitives.Dot(text, pair.Value)))
                .OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(pair => pair.Key),
        ];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        provider.Dispose();
        services.Dispose();
    }

    private async Task<float[]?> EmbedItemAsync(string path, CancellationToken cancellationToken)
    {
        var content = new MediaContent(Id<MediaItem>.New(), DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(File.OpenRead(path))) { LocalPath = path };
        MediaFormatDetection format;
        await using (var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false))
        {
            format = await services.GetRequiredService<IMediaFormatRegistry>().DetectAsync(stream, Path.GetFileName(path), cancellationToken).ConfigureAwait(false);
        }

        if (!format.IsSupported)
        {
            throw new InvalidDataException($"{path} isn't a supported media file: {format.Rejection}");
        }

        if (format.MediaType is MediaType.Video or MediaType.AnimatedImage)
        {
            var metadata = await format.Handler!.MetadataExtractor.ExtractAsync(content, format, cancellationToken).ConfigureAwait(false);
            var sample = await services.GetRequiredService<IFrameSampler>().SampleAsync(content, format, metadata, FrameSamplingOptions.Default, cancellationToken).ConfigureAwait(false);
            if (sample.Frames.Count == 0)
            {
                return null;
            }

            var frames = sample.Frames.Select(frame => AiImage.FromRgba(frame.Pixels, frame.Width, frame.Height, $"frame {frame.Index}")).ToList();
            var embedded = await provider.EmbedImagesAsync(frames, cancellationToken).ConfigureAwait(false);
            return KeyframeAggregation.MeanEmbedding(embedded.Value);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var output = await provider.EmbedImagesAsync([new AiImage(bytes, "image/*", Path.GetFileName(path))], cancellationToken).ConfigureAwait(false);
        return output.Value[0].ToArray();
    }

    private sealed class DirectoryModelStore(string directory) : IModelStore
    {
        public string RootDirectory => directory;

        public Task<InstalledModel?> FindAsync(string modelId, string version, CancellationToken cancellationToken) =>
            Task.FromResult<InstalledModel?>(modelId == Siglip2Model.ModelId && version == Siglip2Model.Version ? Installed() : null);

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordUsageAsync(string modelId, string version, string libraryPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();

        private InstalledModel Installed() => new(
            Siglip2Model.ModelId,
            Siglip2Model.Version,
            "SigLIP 2 base-patch16-256",
            "Apache-2.0",
            "evaluation",
            directory,
            [
                new InstalledModelFile(Siglip2Model.VisionRole, "vision_model.onnx", 0, "0"),
                new InstalledModelFile(Siglip2Model.TextRole, "text_model.onnx", 0, "0"),
                new InstalledModelFile(Siglip2Model.TokenizerRole, "tokenizer.model", 0, "0"),
            ],
            "evaluation",
            DateTimeOffset.UnixEpoch);
    }
}
