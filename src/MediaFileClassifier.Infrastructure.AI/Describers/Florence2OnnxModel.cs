using System.Text.RegularExpressions;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.AI.Describers;

/// <summary>
/// One installed copy of Florence-2, run as in the MFC-105 spike (<c>tools/describer-spike/run_florence.py</c>): the
/// picture is encoded once, then three task prompts each run the text encoder and a greedy decode with the
/// generation config's no-repeated-3-gram rule. <c>&lt;MORE_DETAILED_CAPTION&gt;</c> gives the description,
/// <c>&lt;CAPTION&gt;</c> the summary, and <c>&lt;OD&gt;</c> the objects, whose labels in lower case are also the tags.
/// </summary>
internal sealed partial class Florence2OnnxModel : OnnxModelProvider, IImageDescriber
{
    /// <summary>
    /// The side of the square picture the vision encoder sees.
    /// </summary>
    public const int ImageSize = 768;

    private const string DescriptionPrompt = "Describe with a paragraph what is shown in the image.";
    private const string SummaryPrompt = "What does the image describe?";
    private const string ObjectsPrompt = "Locate the objects with category name in the image.";
    private const int NoRepeatNgram = 3;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    private readonly Florence2Options options;
    private readonly Lazy<Florence2Tokenizer> tokenizer;
    private readonly string tokenizerPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="Florence2OnnxModel"/> class.
    /// </summary>
    /// <param name="variant">The model size.</param>
    /// <param name="installed">The installed model, with the five files of <see cref="Florence2Model"/>.</param>
    /// <param name="sessionFactory">Session factory.</param>
    /// <param name="options">Token limits.</param>
    public Florence2OnnxModel(Florence2Variant variant, InstalledModel installed, OnnxSessionFactory sessionFactory, Florence2Options options)
        : base(variant.ProviderId, variant.DisplayName, CreateIdentity(variant, installed), installed.GetFilePaths(), sessionFactory, ExecutionProvider.Cpu)
    {
        ArgumentNullException.ThrowIfNull(options);
        var paths = installed.GetFilePaths();
        string[] roles = [Florence2Model.VisionRole, Florence2Model.EmbedRole, Florence2Model.EncoderRole, Florence2Model.DecoderRole, Florence2Model.TokenizerRole];
        if (roles.FirstOrDefault(role => !paths.ContainsKey(role)) is { } missing)
        {
            throw new ArgumentException($"The model needs a file for the role {missing}.", nameof(installed));
        }

        this.options = options;
        tokenizerPath = paths[Florence2Model.TokenizerRole];
        tokenizer = new Lazy<Florence2Tokenizer>(() => Florence2Tokenizer.Load(tokenizerPath), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    string IImageDescriber.PromptVersion => Florence2Model.PromptVersion;

    /// <inheritdoc />
    public DescriberFields SupportedFields => Florence2Model.SupportedFields;

    /// <inheritdoc />
    protected override string? PromptVersion => Florence2Model.PromptVersion;

    /// <summary>
    /// Creates the model's identity.
    /// </summary>
    /// <param name="variant">The model size.</param>
    /// <param name="installed">The installed model, or <c>null</c> before it's found.</param>
    /// <returns>The identity.</returns>
    public static ModelIdentity CreateIdentity(Florence2Variant variant, InstalledModel? installed)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return new(ProviderIds.Onnx, variant.ModelId, installed?.Version ?? Florence2Model.Version, installed?.ContentHash);
    }

    /// <inheritdoc />
    public string GetPromptVersion(string? requested) => Florence2Model.PromptVersion;

    /// <inheritdoc />
    public Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        return RunAsync(() => Describe(image, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Loads every session and the tokenizer.
    /// </summary>
    public void Load()
    {
        _ = GetSession(Florence2Model.VisionRole);
        _ = GetSession(Florence2Model.EmbedRole);
        _ = GetSession(Florence2Model.EncoderRole);
        _ = GetSession(Florence2Model.DecoderRole);
        _ = tokenizer.Value;
    }

    /// <summary>
    /// Reads the object labels of an <c>&lt;OD&gt;</c> answer: each label is followed by four location tokens. Labels
    /// repeat once per box, so they're kept once, in order of first appearance, compared without case.
    /// </summary>
    /// <param name="answer">The decoded answer, with its added tokens.</param>
    /// <returns>The labels.</returns>
    internal static List<string> ObjectLabels(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var text = answer.Replace("<s>", string.Empty, StringComparison.Ordinal)
            .Replace("</s>", string.Empty, StringComparison.Ordinal)
            .Replace("<pad>", string.Empty, StringComparison.Ordinal);
        var labels = new List<string>();
        foreach (Match match in LabelPattern().Matches(text))
        {
            var label = match.Groups[1].Value.Trim();
            if (label.Length > 0 && !labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    /// <summary>
    /// The tokens a greedy step may not pick, so no 3-gram repeats (Hugging Face's <c>no_repeat_ngram_size</c>).
    /// </summary>
    /// <param name="tokens">The tokens so far, the decoder start token first.</param>
    /// <returns>The banned tokens.</returns>
    internal static IEnumerable<int> BannedTokens(IReadOnlyList<int> tokens)
    {
        if (tokens.Count < NoRepeatNgram)
        {
            yield break;
        }

        var last = tokens.Count - (NoRepeatNgram - 1);
        for (var i = 0; i + NoRepeatNgram - 1 < tokens.Count; i++)
        {
            var same = true;
            for (var k = 0; k < NoRepeatNgram - 1 && same; k++)
            {
                same = tokens[i + k] == tokens[last + k];
            }

            if (same)
            {
                yield return tokens[i + NoRepeatNgram - 1];
            }
        }
    }

    /// <summary>
    /// Prepares the vision encoder's input: the picture squashed to 768 x 768 with a bicubic filter, scaled to 0–1 and
    /// normalised with the ImageNet mean and deviation, channels first.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <returns>The values, 3 x 768 x 768.</returns>
    internal static float[] PreparePixels(AiImage image)
    {
        var (pixels, width, height) = image.IsRawRgba
            ? (image.Data.ToArray(), image.PixelWidth!.Value, image.PixelHeight!.Value)
            : Siglip2ImagePreprocessor.Decode(image, fullSize: true);

        using var source = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, source.GetPixels(), Math.Min(pixels.Length, width * height * 4));
        using var resized = source.Resize(new SKImageInfo(ImageSize, ImageSize, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.CatmullRom))
            ?? throw new InvalidDataException($"The picture {image.Name} can't be resized.");
        var rgba = resized.GetPixelSpan();
        const int Plane = ImageSize * ImageSize;
        var values = new float[3 * Plane];
        for (var i = 0; i < Plane; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                values[(channel * Plane) + i] = ((rgba[(i * 4) + channel] / 255f) - Mean[channel]) / Deviation[channel];
            }
        }

        return values;
    }

    [GeneratedRegex(@"([^<>]+?)((?:<loc_\d+>){4})", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();

    private ImageDescription Describe(AiImage image, CancellationToken cancellationToken)
    {
        float[] pixels;
        try
        {
            pixels = PreparePixels(image);
        }
        catch (InvalidDataException ex)
        {
            throw new AiProviderException(AiFailureKind.InvalidInput, Id, ex.Message, innerException: ex);
        }

        using var pixelValues = OrtValue.CreateTensorValueFromMemory(pixels, [1, 3, ImageSize, ImageSize]);
        var features = Run(Florence2Model.VisionRole, session => Tensor.RunFirst(session, ["pixel_values"], [pixelValues]));
        var text = tokenizer.Value;
        var description = text.Decode(Generate(features, DescriptionPrompt, options.DescriptionMaxTokens, cancellationToken), skipAdded: true).Trim();
        var summary = text.Decode(Generate(features, SummaryPrompt, options.SummaryMaxTokens, cancellationToken), skipAdded: true).Trim();
        var objects = ObjectLabels(text.Decode(Generate(features, ObjectsPrompt, options.ObjectsMaxTokens, cancellationToken), skipAdded: false));
        var tags = objects.Select(label => label.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Take(options.MaxTags).ToList();
        return new ImageDescription(string.Empty, description, summary, tags, objects, [], [], []);
    }

    private List<int> Generate(Tensor features, string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var promptIds = tokenizer.Value.Encode(prompt);
        var promptEmbeds = Embed(promptIds);
        var inputs = Tensor.Concat(features, promptEmbeds);
        var length = inputs.Shape[1];
        var mask = new long[length];
        Array.Fill(mask, 1L);
        using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, [1, length]);
        using var inputsValue = OrtValue.CreateTensorValueFromMemory(inputs.Values, inputs.Shape);
        var hidden = Run(Florence2Model.EncoderRole, session => Tensor.RunFirst(session, ["inputs_embeds", "attention_mask"], [inputsValue, maskValue]));
        using var hiddenValue = OrtValue.CreateTensorValueFromMemory(hidden.Values, hidden.Shape);
        return Run(Florence2Model.DecoderRole, session => Decode(session, hiddenValue, maskValue, maxTokens, cancellationToken));
    }

    private List<int> Decode(InferenceSession decoder, OrtValue hidden, OrtValue mask, int maxTokens, CancellationToken cancellationToken)
    {
        var pastNames = decoder.InputMetadata.Keys.Where(name => name.StartsWith("past_key_values", StringComparison.Ordinal)).ToList();
        var pastShape = decoder.InputMetadata[pastNames[0]].Dimensions;
        var outputNames = decoder.OutputMetadata.Keys.ToList();
        var logitsIndex = outputNames.IndexOf("logits");
        var presentIndex = pastNames.Select(name => outputNames.IndexOf(name.Replace("past_key_values", "present", StringComparison.Ordinal))).ToList();
        var inputNames = new List<string> { "encoder_attention_mask", "encoder_hidden_states", "inputs_embeds", "use_cache_branch" };
        inputNames.AddRange(pastNames);

        using var runOptions = new RunOptions();
        using var empty = OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [1, pastShape[1], 0, pastShape[3]]);
        IDisposableReadOnlyCollection<OrtValue>? first = null;
        IDisposableReadOnlyCollection<OrtValue>? previous = null;
        var tokens = new List<int> { Florence2Tokenizer.EndId };
        try
        {
            for (var step = 0; step < maxTokens; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var useCache = step > 0;
                var embeds = Embed(useCache ? [tokens[^1]] : [.. tokens.Select(token => (long)token)]);
                using var embedsValue = OrtValue.CreateTensorValueFromMemory(embeds.Values, embeds.Shape);
                using var cacheFlag = OrtValue.CreateTensorValueFromMemory(new[] { useCache }, [1]);
                var values = new List<OrtValue> { mask, hidden, embedsValue, cacheFlag };
                for (var i = 0; i < pastNames.Count; i++)
                {
                    // Once the cache is used, the merged decoder returns no new encoder keys, so the first ones stay.
                    var source = pastNames[i].Contains(".encoder.", StringComparison.Ordinal) ? first : previous;
                    values.Add(source?[presentIndex[i]] ?? empty);
                }

                var outputs = decoder.Run(runOptions, inputNames, values, outputNames);
                var logits = outputs[logitsIndex];
                var shape = logits.GetTensorTypeAndShape().Shape;
                var vocabulary = (int)shape[2];
                var row = logits.GetTensorDataAsSpan<float>().Slice((int)((shape[1] - 1) * vocabulary), vocabulary).ToArray();
                if (first is null)
                {
                    first = outputs;
                }
                else if (!ReferenceEquals(previous, first))
                {
                    previous?.Dispose();
                }

                previous = outputs;
                var next = step == 0 ? Florence2Tokenizer.BeginId : PickGreedy(row, tokens);
                tokens.Add(next);
                if (next == Florence2Tokenizer.EndId)
                {
                    break;
                }
            }
        }
        finally
        {
            if (!ReferenceEquals(previous, first))
            {
                previous?.Dispose();
            }

            first?.Dispose();
        }

        return tokens.Skip(1).ToList();
    }

    private static int PickGreedy(float[] logits, List<int> tokens)
    {
        foreach (var banned in BannedTokens(tokens))
        {
            logits[banned] = float.NegativeInfinity;
        }

        var best = 0;
        for (var i = 1; i < logits.Length; i++)
        {
            if (logits[i] > logits[best])
            {
                best = i;
            }
        }

        return best;
    }

    private Tensor Embed(long[] ids)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
        return Run(Florence2Model.EmbedRole, session => Tensor.RunFirst(session, ["input_ids"], [input]));
    }

    /// <summary>
    /// A float tensor copied out of ONNX Runtime.
    /// </summary>
    /// <param name="Values">The values.</param>
    /// <param name="Shape">The shape.</param>
    private sealed record Tensor(float[] Values, long[] Shape)
    {
        public static Tensor RunFirst(InferenceSession session, IReadOnlyCollection<string> inputNames, IReadOnlyCollection<OrtValue> inputs)
        {
            using var runOptions = new RunOptions();
            using var outputs = session.Run(runOptions, inputNames, inputs, [session.OutputMetadata.Keys.First()]);
            var value = outputs[0];
            return new Tensor(value.GetTensorDataAsSpan<float>().ToArray(), value.GetTensorTypeAndShape().Shape);
        }

        /// <summary>
        /// Joins two [1, n, d] tensors along the sequence.
        /// </summary>
        public static Tensor Concat(Tensor first, Tensor second)
        {
            if (first.Shape[2] != second.Shape[2])
            {
                throw new InvalidDataException($"The image features ({first.Shape[2]}) and the prompt embedding ({second.Shape[2]}) differ in width.");
            }

            return new Tensor([.. first.Values, .. second.Values], [1, first.Shape[1] + second.Shape[1], first.Shape[2]]);
        }
    }
}
