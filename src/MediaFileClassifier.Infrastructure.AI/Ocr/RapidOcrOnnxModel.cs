using System.Numerics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// One installed copy of the PP-OCR model, run the way RapidOCR runs it: the DB detector finds text boxes, each box
/// is cut out upright, and the CTC recogniser reads it. There's no angle classifier, so upside-down text isn't read.
/// </summary>
internal sealed class RapidOcrOnnxModel : OnnxModelProvider, IOcrProvider
{
    private const int RecognitionHeight = 48;
    private const int RecognitionMinWidth = 320;

    private readonly RapidOcrOptions options;
    private readonly TextDetectionOptions detection = new();
    private readonly Lazy<CtcDecoder> decoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="RapidOcrOnnxModel"/> class.
    /// </summary>
    /// <param name="installed">The installed model, with a detector and a recogniser file.</param>
    /// <param name="sessionFactory">Session factory.</param>
    /// <param name="preferredExecutionProvider">Execution provider to try first.</param>
    /// <param name="options">Thresholds and sizes.</param>
    public RapidOcrOnnxModel(InstalledModel installed, OnnxSessionFactory sessionFactory, ExecutionProvider preferredExecutionProvider, RapidOcrOptions options)
        : base(RapidOcrModel.ProviderId, installed.DisplayName, CreateIdentity(installed), installed.GetFilePaths(), sessionFactory, preferredExecutionProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        var paths = installed.GetFilePaths();
        if (!paths.ContainsKey(RapidOcrModel.DetectorRole) || !paths.ContainsKey(RapidOcrModel.RecognizerRole))
        {
            throw new ArgumentException($"The model needs the files {RapidOcrModel.DetectorRole} and {RapidOcrModel.RecognizerRole}.", nameof(installed));
        }

        this.options = options;
        decoder = new Lazy<CtcDecoder>(LoadDecoder, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    protected override string? PromptVersion => RapidOcrModel.ProcessingVersion;

    /// <summary>
    /// Creates the model's identity.
    /// </summary>
    /// <param name="installed">The installed model, or <c>null</c> before it's found.</param>
    /// <returns>The identity.</returns>
    public static ModelIdentity CreateIdentity(InstalledModel? installed) =>
        new(ProviderIds.Onnx, RapidOcrModel.ModelId, installed?.Version ?? RapidOcrModel.Version, installed?.ContentHash);

    /// <inheritdoc />
    public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RunAsync(() => Recognize(image, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Finds the text boxes in an image, without reading them.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The boxes, in image pixels.</returns>
    public Task<AiOutput<IReadOnlyList<TextBox>>> DetectAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        return RunAsync<IReadOnlyList<TextBox>>(() => Detect(ToPicture(image)), cancellationToken);
    }

    /// <summary>
    /// Loads both sessions and the dictionary.
    /// </summary>
    public void Load()
    {
        _ = GetSession(RapidOcrModel.DetectorRole);
        _ = decoder.Value;
    }

    /// <summary>
    /// Gets the detector's input size for a picture: the short side enlarged to the minimum, the long side capped, both
    /// rounded to a multiple of 32.
    /// </summary>
    /// <param name="width">Picture width.</param>
    /// <param name="height">Picture height.</param>
    /// <param name="minSide">Least short side.</param>
    /// <param name="maxSide">Most long side.</param>
    /// <returns>The detector's input size.</returns>
    internal static (int Width, int Height) DetectionSize(int width, int height, int minSide, int maxSide)
    {
        var ratio = Math.Min(width, height) < minSide ? (double)minSide / Math.Min(width, height) : 1;
        if (Math.Max(width, height) * ratio > maxSide)
        {
            ratio = (double)maxSide / Math.Max(width, height);
        }

        static int Round32(double value) => Math.Max(32, (int)Math.Round(value / 32) * 32);
        return (Round32(width * ratio), Round32(height * ratio));
    }

    private static RgbaPicture ToPicture(AiImage image)
    {
        if (image.IsRawRgba)
        {
            return new RgbaPicture(image.Data.ToArray(), image.PixelWidth!.Value, image.PixelHeight!.Value);
        }

        try
        {
            var (pixels, width, height) = Siglip2ImagePreprocessor.Decode(image, fullSize: true);
            return new RgbaPicture(pixels, width, height);
        }
        catch (InvalidDataException ex)
        {
            throw new AiProviderException(AiFailureKind.InvalidInput, RapidOcrModel.ProviderId, ex.Message, innerException: ex);
        }
    }

    private CtcDecoder LoadDecoder()
    {
        var session = GetSession(RapidOcrModel.RecognizerRole).Session;
        if (!session.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var characters))
        {
            throw new AiProviderException(AiFailureKind.ProviderError, Id, "The recogniser has no character dictionary in its metadata.");
        }

        return CtcDecoder.FromMetadata(characters);
    }

    private List<TextBox> Detect(RgbaPicture picture)
    {
        var (width, height) = DetectionSize(picture.Width, picture.Height, options.DetectionMinSide, options.DetectionMaxSide);
        var resized = OcrImageOps.Resize(picture, width, height);
        var input = new DenseTensor<float>([1, 3, height, width]);
        OcrImageOps.WriteBgr(resized, input.Buffer.Span, 0, width, height);
        var map = Run(RapidOcrModel.DetectorRole, session =>
        {
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
            return results[0].AsTensor<float>().ToArray();
        });

        if (map.Length != width * height)
        {
            throw new AiProviderException(AiFailureKind.InvalidOutput, Id, $"The detector returned {map.Length} values for a {width} x {height} picture.");
        }
        var scaleX = (float)picture.Width / width;
        var scaleY = (float)picture.Height / height;
        return TextBoxDetector.FindBoxes(map, width, height, detection)
            .Select(box => box with
            {
                Corners = box.Corners
                    .Select(point => new Vector2(Math.Clamp(MathF.Round(point.X * scaleX), 0, picture.Width), Math.Clamp(MathF.Round(point.Y * scaleY), 0, picture.Height)))
                    .ToArray(),
            })
            .ToList();
    }

    private OcrResult Recognize(AiImage image, CancellationToken cancellationToken)
    {
        var picture = ToPicture(image);
        var boxes = Detect(picture);
        var lines = new List<(TextBox Box, RgbaPicture Crop)>(boxes.Count);
        foreach (var box in boxes)
        {
            if (OcrImageOps.CropBox(picture, box.Corners) is { } crop)
            {
                lines.Add((box, crop));
            }
        }

        // Lines of similar shape share a batch, so little of it is padding.
        var order = lines.Select((line, index) => (Ratio: (float)line.Crop.Width / line.Crop.Height, Index: index)).OrderBy(line => line.Ratio).ToList();
        var texts = new (string Text, float Confidence)[lines.Count];
        for (var start = 0; start < order.Count; start += options.RecognitionBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = order.Skip(start).Take(options.RecognitionBatchSize).ToList();
            var read = ReadBatch(batch.Select(line => lines[line.Index].Crop).ToList());
            for (var i = 0; i < batch.Count; i++)
            {
                texts[batch[i].Index] = read[i];
            }
        }
        var kept = new List<(TextBox Box, string Text, float Confidence)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var (text, confidence) = texts[i];
            text = text.Trim();
            if (text.Length > 0 && confidence >= options.MinTextConfidence)
            {
                kept.Add((lines[i].Box, text, confidence));
            }
        }

        var regions = kept
            .Select(line => new OcrRegion(
                line.Text,
                Math.Round(line.Confidence, 4),
                line.Box.Corners.Select(point => new OcrPoint(point.X / picture.Width, point.Y / picture.Height)).ToList()))
            .ToList();
        return new OcrResult(OcrLineLayout.Join(kept.Select(line => (line.Box.Corners, line.Text)).ToList())) { Regions = regions };
    }

    private (string Text, float Confidence)[] ReadBatch(List<RgbaPicture> crops)
    {
        var maxRatio = Math.Max((float)RecognitionMinWidth / RecognitionHeight, crops.Max(crop => (float)crop.Width / crop.Height));
        var width = (int)(RecognitionHeight * maxRatio);
        var plane = RecognitionHeight * width;
        var input = new DenseTensor<float>([crops.Count, 3, RecognitionHeight, width]);
        for (var i = 0; i < crops.Count; i++)
        {
            var crop = crops[i];
            var resizedWidth = Math.Clamp((int)Math.Ceiling(RecognitionHeight * (float)crop.Width / crop.Height), 1, width);
            OcrImageOps.WriteBgr(OcrImageOps.Resize(crop, resizedWidth, RecognitionHeight), input.Buffer.Span, i * 3 * plane, width, RecognitionHeight);
        }

        var ctc = decoder.Value;
        var (output, steps, classes) = Run(RapidOcrModel.RecognizerRole, session =>
        {
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
            var tensor = results[0].AsTensor<float>();
            return (tensor.ToArray(), tensor.Dimensions[1], tensor.Dimensions[2]);
        });

        if (classes != ctc.ClassCount)
        {
            throw new AiProviderException(AiFailureKind.InvalidOutput, Id, $"The recogniser returned {classes} classes, but its dictionary has {ctc.ClassCount}.");
        }

        var read = new (string Text, float Confidence)[crops.Count];
        for (var i = 0; i < crops.Count; i++)
        {
            read[i] = ctc.Decode(output.AsSpan(i * steps * classes, steps * classes), steps);
        }

        return read;
    }
}
