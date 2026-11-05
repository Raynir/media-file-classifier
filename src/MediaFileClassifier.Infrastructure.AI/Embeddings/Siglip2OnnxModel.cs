using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// The two SigLIP 2 towers of one installed model. <see cref="Siglip2EmbeddingProvider"/> creates it once the model
/// is in the store and forwards every call to it.
/// </summary>
internal sealed class Siglip2OnnxModel : OnnxModelProvider, IImageEmbeddingProvider, ITextEmbeddingProvider
{
    private const string PixelInput = "pixel_values";
    private const string TokenInput = "input_ids";
    private const string PooledOutput = "pooler_output";

    private readonly string tokenizerPath;
    private readonly int imageBatchSize;
    private readonly Lazy<Siglip2Tokenizer> tokenizer;

    public Siglip2OnnxModel(InstalledModel installed, OnnxSessionFactory sessionFactory, ExecutionProvider preferredExecutionProvider, int imageBatchSize)
        : base(
            Siglip2Model.ProviderId,
            installed.DisplayName,
            CreateIdentity(installed),
            installed.GetFilePaths(),
            sessionFactory,
            preferredExecutionProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageBatchSize);
        var paths = installed.GetFilePaths();
        if (!paths.ContainsKey(Siglip2Model.VisionRole) || !paths.ContainsKey(Siglip2Model.TextRole) || !paths.TryGetValue(Siglip2Model.TokenizerRole, out var tokenizerFile))
        {
            throw new ArgumentException(
                $"The model needs the files {Siglip2Model.VisionRole}, {Siglip2Model.TextRole} and {Siglip2Model.TokenizerRole}.",
                nameof(installed));
        }

        tokenizerPath = tokenizerFile;
        this.imageBatchSize = imageBatchSize;
        tokenizer = new Lazy<Siglip2Tokenizer>(() => Siglip2Tokenizer.Load(tokenizerPath), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Dimension => Siglip2Model.Dimension;

    protected override string? PromptVersion => Siglip2Model.PreprocessingVersion;

    public static ModelIdentity CreateIdentity(InstalledModel? installed) =>
        new(ProviderIds.Onnx, Siglip2Model.ModelId, installed?.Version ?? Siglip2Model.Version, installed?.ContentHash, Siglip2Model.Dimension);

    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        return RunAsync<IReadOnlyList<ReadOnlyMemory<float>>>(() => EmbedImages(images, cancellationToken), cancellationToken);
    }

    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return RunAsync<IReadOnlyList<ReadOnlyMemory<float>>>(() => EmbedTexts(texts, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Loads the sessions of one or both towers, so the first real call doesn't pay for it.
    /// </summary>
    /// <param name="capability">The tower to load, or <c>null</c> for both.</param>
    public void Load(AiCapability? capability)
    {
        if (capability is null or AiCapability.ImageEmbedding)
        {
            _ = GetSession(Siglip2Model.VisionRole);
        }

        if (capability is null or AiCapability.TextEmbedding)
        {
            _ = GetSession(Siglip2Model.TextRole);
            _ = tokenizer.Value;
        }
    }

    private static ReadOnlyMemory<float>[] Split(Tensor<float> pooled, int count)
    {
        var vectors = new ReadOnlyMemory<float>[count];
        var values = pooled.ToArray();
        for (var i = 0; i < count; i++)
        {
            vectors[i] = Normalize(values.AsSpan(i * Siglip2Model.Dimension, Siglip2Model.Dimension));
        }

        return vectors;
    }

    private static float[] Normalize(ReadOnlySpan<float> vector)
    {
        var sum = 0.0;
        foreach (var value in vector)
        {
            sum += (double)value * value;
        }

        if (sum == 0 || !double.IsFinite(sum))
        {
            throw new AiProviderException(AiFailureKind.InvalidOutput, Siglip2Model.ProviderId, "The model returned a zero or non-finite vector.");
        }

        var norm = (float)Math.Sqrt(sum);
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / norm;
        }

        return result;
    }

    private ReadOnlyMemory<float>[] EmbedImages(IReadOnlyList<AiImage> images, CancellationToken cancellationToken)
    {
        var session = GetSession(Siglip2Model.VisionRole).Session;
        var vectors = new ReadOnlyMemory<float>[images.Count];
        for (var start = 0; start < images.Count; start += imageBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(imageBatchSize, images.Count - start);
            var input = new float[count * Siglip2ImagePreprocessor.ValuesPerImage];
            for (var i = 0; i < count; i++)
            {
                var image = images[start + i];
                try
                {
                    Siglip2ImagePreprocessor.Prepare(image, input.AsSpan(i * Siglip2ImagePreprocessor.ValuesPerImage, Siglip2ImagePreprocessor.ValuesPerImage));
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
                {
                    throw new AiProviderException(AiFailureKind.InvalidInput, Id, $"The picture {image.Name} can't be prepared: {ex.Message}", innerException: ex);
                }
            }

            var tensor = new DenseTensor<float>(input, [count, 3, Siglip2Model.ImageSize, Siglip2Model.ImageSize]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(PixelInput, tensor)], [PooledOutput]);
            Split(results[0].AsTensor<float>(), count).CopyTo(vectors, start);
        }

        return vectors;
    }

    private ReadOnlyMemory<float>[] EmbedTexts(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(Siglip2Model.TextRole).Session;
        var ids = new long[texts.Count * Siglip2Model.SequenceLength];
        for (var i = 0; i < texts.Count; i++)
        {
            tokenizer.Value.Encode(texts[i], ids.AsSpan(i * Siglip2Model.SequenceLength, Siglip2Model.SequenceLength));
        }

        var tensor = new DenseTensor<long>(ids, [texts.Count, Siglip2Model.SequenceLength]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(TokenInput, tensor)], [PooledOutput]);
        return Split(results[0].AsTensor<float>(), texts.Count);
    }
}
