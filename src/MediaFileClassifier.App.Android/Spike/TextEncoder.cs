using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// A provisional multilingual text encoder on ONNX Runtime (MFC-18). MFC-53 picks the real model; this only proves
/// that a tokenizer and an encoder run on the device. No Android APIs, so a desktop console can run the same code
/// to compare the embeddings.
/// </summary>
internal sealed class TextEncoder : IDisposable
{
    private readonly InferenceSession session;
    private readonly SentencePieceTokenizer tokenizer;
    private readonly EncoderKind kind;
    private readonly string outputName;

    private TextEncoder(InferenceSession session, SentencePieceTokenizer tokenizer, EncoderKind kind)
    {
        this.session = session;
        this.tokenizer = tokenizer;
        this.kind = kind;
        outputName = session.OutputMetadata.ContainsKey("pooler_output") ? "pooler_output" : session.OutputMetadata.Keys.First();
    }

    /// <summary>
    /// The candidate models, by folder name.
    /// </summary>
    public enum EncoderKind
    {
        /// <summary>
        /// <c>intfloat/multilingual-e5-small</c>, int8: XLM-R tokenizer, mean pooling, 384 dimensions. Text only.
        /// </summary>
        E5Small,

        /// <summary>
        /// <c>google/siglip2-base-patch16-224</c> text tower, int8: Gemma tokenizer, pooled output, 768 dimensions.
        /// </summary>
        Siglip2Base,
    }

    /// <summary>
    /// Gets how long loading the model took.
    /// </summary>
    public TimeSpan LoadTime { get; private init; }

    /// <summary>
    /// Gets the model file's size in bytes.
    /// </summary>
    public long ModelBytes { get; private init; }

    /// <summary>
    /// Gets the names of the model inputs.
    /// </summary>
    public IEnumerable<string> InputNames => session.InputMetadata.Keys;

    /// <summary>
    /// Gets the output used as the embedding.
    /// </summary>
    public string OutputName => outputName;

    /// <summary>
    /// Parses a model name as used in the spike scripts: <c>e5</c> or <c>siglip2</c>.
    /// </summary>
    /// <param name="name">Model name.</param>
    /// <returns>The model.</returns>
    public static EncoderKind ParseKind(string name) => name switch
    {
        "e5" => EncoderKind.E5Small,
        "siglip2" => EncoderKind.Siglip2Base,
        _ => throw new ArgumentException($"Unknown model '{name}'.", nameof(name)),
    };

    /// <summary>
    /// Gets the folder name of a model.
    /// </summary>
    /// <param name="kind">Model.</param>
    /// <returns>The folder name.</returns>
    public static string FolderName(EncoderKind kind) => kind == EncoderKind.E5Small ? "e5" : "siglip2";

    /// <summary>
    /// Loads a model and its tokenizer.
    /// </summary>
    /// <param name="modelsDirectory">Folder with one subfolder per model.</param>
    /// <param name="kind">Model to load.</param>
    /// <param name="provider">Execution provider: <c>cpu</c>, <c>nnapi</c> or <c>xnnpack</c>.</param>
    /// <returns>The encoder.</returns>
    public static TextEncoder Load(string modelsDirectory, EncoderKind kind, string provider)
    {
        var directory = Path.Combine(modelsDirectory, FolderName(kind));
        var (modelFile, tokenizerFile) = kind == EncoderKind.E5Small
            ? ("model_int8.onnx", "sentencepiece.bpe.model")
            : ("text_model_int8.onnx", "tokenizer.model");
        var modelPath = Path.Combine(directory, modelFile);

        var stopwatch = Stopwatch.StartNew();
        OrtEnv.Instance().DisableTelemetryEvents();
        using var options = CreateOptions(provider);
        var session = new InferenceSession(modelPath, options);
        var loadTime = stopwatch.Elapsed;

        using var tokenizerStream = File.OpenRead(Path.Combine(directory, tokenizerFile));
        var tokenizer = kind == EncoderKind.E5Small
            ? SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: false, addEndOfSentence: false)
            : SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: false, addEndOfSentence: true);
        return new TextEncoder(session, tokenizer, kind)
        {
            LoadTime = loadTime,
            ModelBytes = new FileInfo(modelPath).Length,
        };
    }

    /// <summary>
    /// Gets the token IDs the model sees for a text.
    /// </summary>
    /// <param name="text">Query text.</param>
    /// <returns>The token IDs.</returns>
    public IReadOnlyList<long> Tokenize(string text)
    {
        if (kind == EncoderKind.E5Small)
        {
            // XLM-R (fairseq) IDs: <s>=0, <pad>=1, </s>=2, <unk>=3, then SentencePiece IDs shifted by one.
            var pieces = tokenizer.EncodeToIds("query: " + text, addBeginningOfSentence: false, addEndOfSentence: false);
            return [0, .. pieces.Take(510).Select(id => id == 0 ? 3L : id + 1L), 2];
        }

        // SigLIP 2: lower case, </eos> appended, padded with <pad>=0 to 64 tokens.
        const int Length = 64;
        var ids = tokenizer.EncodeToIds(text.ToLowerInvariant(), addBeginningOfSentence: false, addEndOfSentence: true)
            .Take(Length)
            .Select(id => (long)id)
            .ToList();
        ids.AddRange(Enumerable.Repeat(0L, Length - ids.Count));
        return ids;
    }

    /// <summary>
    /// Embeds a text.
    /// </summary>
    /// <param name="text">Query text.</param>
    /// <returns>The L2-normalized embedding.</returns>
    public float[] Embed(string text)
    {
        var ids = Tokenize(text);
        var shape = new[] { 1, ids.Count };
        var inputs = new List<NamedOnnxValue>();
        foreach (var name in session.InputMetadata.Keys)
        {
            var values = name switch
            {
                "input_ids" => ids.ToArray(),
                "attention_mask" => Enumerable.Repeat(1L, ids.Count).ToArray(),
                "token_type_ids" => new long[ids.Count],
                _ => throw new NotSupportedException($"Unexpected model input '{name}'."),
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, shape)));
        }

        using var results = session.Run(inputs, [outputName]);
        var output = results[0].AsTensor<float>();
        var embedding = output.Rank == 3 ? MeanPool(output) : output.ToArray();
        return Normalize(embedding);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.Dispose();
    }

    /// <summary>
    /// Gets the cosine similarity of two normalized embeddings.
    /// </summary>
    /// <param name="a">First embedding.</param>
    /// <param name="b">Second embedding.</param>
    /// <returns>The similarity.</returns>
    public static float Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();

    private static SessionOptions CreateOptions(string provider)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        switch (provider)
        {
            case "cpu":
                break;
            case "nnapi":
                options.AppendExecutionProvider_Nnapi(NnapiFlags.NNAPI_FLAG_USE_NONE);
                break;
            case "xnnpack":
                // XNNPACK has its own thread pool; ONNX Runtime recommends one intra-op thread next to it.
                options.IntraOpNumThreads = 1;
                options.AppendExecutionProvider("XNNPACK", new Dictionary<string, string> { ["intra_op_num_threads"] = "4" });
                break;
            default:
                throw new ArgumentException($"Unknown execution provider '{provider}'.", nameof(provider));
        }

        return options;
    }

    private static float[] MeanPool(Tensor<float> hiddenStates)
    {
        var tokens = hiddenStates.Dimensions[1];
        var size = hiddenStates.Dimensions[2];
        var pooled = new float[size];
        for (var t = 0; t < tokens; t++)
        {
            for (var d = 0; d < size; d++)
            {
                pooled[d] += hiddenStates[0, t, d] / tokens;
            }
        }

        return pooled;
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        return vector.Select(value => value / norm).ToArray();
    }
}
