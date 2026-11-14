using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;

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
    private readonly CorpusMedia media = new();
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

    /// <summary>
    /// Gets the vector of each indexed item, by item ID.
    /// </summary>
    internal IReadOnlyDictionary<string, float[]> Vectors => vectors;

    /// <summary>
    /// Encodes a text with the text tower, as given.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The normalised vector.</returns>
    internal async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        var output = await provider.EmbedTextsAsync([text], cancellationToken).ConfigureAwait(false);
        return output.Value[0].ToArray();
    }

    /// <summary>
    /// Scores every indexed item against a text query, for picking the relevance threshold (E09-S01-T03).
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cosine similarity of each item, by item ID.</returns>
    public async Task<IReadOnlyDictionary<string, float>> ScoreAsync(string text, CancellationToken cancellationToken)
    {
        var output = await provider.EmbedTextsAsync([text], cancellationToken).ConfigureAwait(false);
        var query = output.Value[0].ToArray();
        return vectors.ToDictionary(pair => pair.Key, pair => System.Numerics.Tensors.TensorPrimitives.Dot(query, pair.Value), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        provider.Dispose();
        media.Dispose();
    }

    private async Task<float[]?> EmbedItemAsync(string path, CancellationToken cancellationToken)
    {
        var file = await media.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        if (file.Frames is { } frames)
        {
            if (frames.Count == 0)
            {
                return null;
            }

            var embedded = await provider.EmbedImagesAsync(frames, cancellationToken).ConfigureAwait(false);
            return KeyframeAggregation.MeanEmbedding(embedded.Value);
        }

        var output = await provider.EmbedImagesAsync([new AiImage(file.Bytes!, "image/*", Path.GetFileName(path))], cancellationToken).ConfigureAwait(false);
        return output.Value[0].ToArray();
    }

    private sealed class DirectoryModelStore(string directory) : IModelStore
    {
        public event EventHandler? ModelsChanged
        {
            add { }
            remove { }
        }

        public string RootDirectory => directory;

        public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
            Task.FromResult<InstalledModel?>(IsSiglip2(modelId, version) && tierId == TierId ? Installed() : null);

        public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledModel>>(IsSiglip2(modelId, version) ? [Installed()] : []);

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();

        private const string TierId = "standard";

        private static bool IsSiglip2(string modelId, string version) => modelId == Siglip2Model.ModelId && version == Siglip2Model.Version;

        private InstalledModel Installed() => new(
            Siglip2Model.ModelId,
            Siglip2Model.Version,
            TierId,
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
