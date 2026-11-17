using System.Security.Cryptography;
using System.Text;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// A fake <see cref="IImageDescriber"/>. By default the description is derived from the image's name and bytes.
/// </summary>
/// <param name="behavior">Latency, failures and health.</param>
/// <param name="describe">Produces the description, or <c>null</c> for the default.</param>
/// <param name="modelName">The model name, e.g. another one to re-index with; the provider ID ends with it.</param>
/// <param name="promptVersion">The prompt version.</param>
/// <param name="supportedFields">The fields it declares (E06-S04-T03); it still returns all of them, as a model might.</param>
public sealed class FakeImageDescriber(
    FakeProviderBehavior? behavior = null,
    Func<AiImage, ImageDescriptionOptions, ImageDescription>? describe = null,
    string modelName = FakeImageDescriber.DefaultModelName,
    string promptVersion = "fake-1",
    DescriberFields supportedFields = DescriberFields.All)
    : FakeAiProvider(
        modelName == FakeImageDescriber.DefaultModelName ? "fake:describe" : $"fake:describe:{modelName}",
        new ModelIdentity(ProviderIds.Fake, modelName, "1"),
        behavior,
        promptVersion), IImageDescriber
{
    /// <summary>
    /// The default model name.
    /// </summary>
    public const string DefaultModelName = "fake-describer";

    /// <inheritdoc />
    public string PromptVersion { get; } = promptVersion;

    /// <inheritdoc />
    public DescriberFields SupportedFields { get; } = supportedFields;

    /// <inheritdoc />
    /// <remarks>The fake has a template for every version: it uses the one asked for.</remarks>
    public string GetPromptVersion(string? requested) => requested ?? PromptVersion;

    /// <inheritdoc />
    public async Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        var output = await RunAsync(() => (describe ?? Describe)(image, options), cancellationToken).ConfigureAwait(false);
        return output with { PromptVersion = GetPromptVersion(options.PromptVersion) };
    }

    private static ImageDescription Describe(AiImage image, ImageDescriptionOptions options)
    {
        var name = image.Name ?? "image";
        var key = FakeHash.Hex(image.Data.Span)[..8];
        return new ImageDescription(
            $"Image {name}",
            $"A fake description of {name} ({key}).",
            $"Summary of {name}.",
            [name, key],
            ["object"],
            ["scene"],
            ["action"],
            options.Language is null ? [] : [options.Language]);
    }
}

/// <summary>
/// A fake <see cref="IOcrProvider"/>. By default it finds no text.
/// </summary>
/// <param name="behavior">Latency, failures and health.</param>
/// <param name="recognize">Produces the text, or <c>null</c> for none.</param>
public sealed class FakeOcrProvider(FakeProviderBehavior? behavior = null, Func<AiImage, string>? recognize = null)
    : FakeAiProvider("fake:ocr", new ModelIdentity(ProviderIds.Fake, "fake-ocr", "1"), behavior), IOcrProvider
{
    /// <inheritdoc />
    public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        return RunAsync(() => new OcrResult(recognize?.Invoke(image) ?? string.Empty), cancellationToken);
    }
}

/// <summary>
/// A fake in-process OCR provider that is also an <see cref="ITextDetector"/>, like the RapidOCR provider. It finds no text by default.
/// </summary>
/// <param name="hasText">Says whether an image has text, or <c>null</c> for none. It may throw to simulate a missing model.</param>
public sealed class FakeDetectingOcrProvider(Func<AiImage, bool>? hasText = null)
    : FakeAiProvider("fake:ocr-detector", new ModelIdentity(ProviderIds.Fake, "fake-ocr-detector", "1"), null), IOcrProvider, ITextDetector
{
    /// <summary>Gets how many images the detector checked.</summary>
    public int DetectorCalls { get; private set; }

    /// <inheritdoc />
    public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken) =>
        RunAsync(() => new OcrResult(string.Empty), cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        DetectorCalls++;
        return Task.FromResult(hasText?.Invoke(image) ?? false);
    }
}

/// <summary>
/// A fake <see cref="ITranscriptionProvider"/>. By default the transcript names the file, in one segment.
/// </summary>
/// <param name="behavior">Latency, failures and health.</param>
/// <param name="transcribe">Produces the transcript, or <c>null</c> for the default.</param>
public sealed class FakeTranscriptionProvider(FakeProviderBehavior? behavior = null, Func<AiAudio, Transcript>? transcribe = null)
    : FakeAiProvider("fake:transcribe", new ModelIdentity(ProviderIds.Fake, "fake-transcriber", "1"), behavior), ITranscriptionProvider
{
    /// <inheritdoc />
    public Task<AiOutput<Transcript>> TranscribeAsync(AiAudio audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);

        return RunAsync(() => transcribe?.Invoke(audio) ?? Transcribe(audio), cancellationToken);
    }

    private static Transcript Transcribe(AiAudio audio)
    {
        var text = $"Speech in {Path.GetFileName(audio.FilePath)}.";
        return new Transcript(text, audio.LanguageHint ?? "en", [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), text)]);
    }
}

/// <summary>
/// A fake two-tower embedding model: one provider for both images and text, in one vector space. Each vector is a
/// normalized pseudo-random vector seeded by a key, so equal keys give equal vectors. An image's key is its bytes by
/// default; a text's key is the text in lower case. To make a query find an image, give
/// <paramref name="imageKey"/> a function that returns the query's text for that image (e.g. its name).
/// </summary>
/// <param name="behavior">Latency, failures and health.</param>
/// <param name="dimension">Vector length.</param>
/// <param name="modelName">Model name, to fake a model change.</param>
/// <param name="imageKey">Key of an image, or <c>null</c> to use its bytes.</param>
public sealed class FakeEmbeddingProvider(
    FakeProviderBehavior? behavior = null,
    int dimension = 8,
    string modelName = "fake-embedder",
    Func<AiImage, string>? imageKey = null)
    : FakeAiProvider($"fake:{modelName}", new ModelIdentity(ProviderIds.Fake, modelName, "1", embeddingDimension: dimension), behavior),
    IImageEmbeddingProvider,
    ITextEmbeddingProvider
{
    /// <inheritdoc />
    public int Dimension => dimension;

    /// <summary>
    /// Gets the vector for a key, as the provider computes it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="dimension">Vector length.</param>
    /// <returns>The normalized vector.</returns>
    public static float[] VectorFor(string key, int dimension)
    {
        ArgumentNullException.ThrowIfNull(key);

        var random = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0));
        var vector = new float[dimension];
        for (var index = 0; index < dimension; index++)
        {
            vector[index] = (float)((random.NextDouble() * 2) - 1);
        }

        var length = MathF.Sqrt(vector.Sum(value => value * value));
        for (var index = 0; index < dimension; index++)
        {
            vector[index] /= length;
        }

        return vector;
    }

    /// <inheritdoc />
    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);

        return RunAsync<IReadOnlyList<ReadOnlyMemory<float>>>(
            () => [.. images.Select(image => (ReadOnlyMemory<float>)VectorFor(imageKey?.Invoke(image) ?? FakeHash.Hex(image.Data.Span), dimension))],
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return RunAsync<IReadOnlyList<ReadOnlyMemory<float>>>(
            () => [.. texts.Select(text => (ReadOnlyMemory<float>)VectorFor(text.ToLowerInvariant(), dimension))],
            cancellationToken);
    }
}

/// <summary>
/// Hashing for the fakes.
/// </summary>
internal static class FakeHash
{
    public static string Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
