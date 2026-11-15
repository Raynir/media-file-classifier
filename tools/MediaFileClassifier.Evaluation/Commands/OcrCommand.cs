using System.Diagnostics;
using System.Text.Json;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Ocr;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Runs one OCR engine over the images with OCR ground truth and over the photos without text tags (E06-S05-T01),
/// with the input the app gives it (a still image at 2048 px on its longest edge), and writes a results file that
/// <c>evaluate --results</c> scores for CER. It prints the time per image and the no-text candidates that got text,
/// which are checked by eye: real incidental text, or a hallucination.
/// </summary>
internal static class OcrCommand
{
    private const int LongestEdge = 2048;

    private static readonly string[] TextTags = ["text", "sign"];

    public static async Task<int> RunAsync(CommandOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var data = EvaluateCommand.Load(options);
        var truth = data.OcrTruth.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var items = data.Items.Where(item => truth.Contains(item.Id)).ToList();
        var candidates = data.Items
            .Where(item => item.Kind == "photo" && !truth.Contains(item.Id) && !item.Tags.Any(tag => TextTags.Any(tag.Contains)))
            .ToList();

        var (engine, name, dispose) = CreateEngine(options);
        try
        {
            using var media = new CorpusMedia();
            var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var times = new List<double>();
            var withText = new List<string>();
            foreach (var item in items.Concat(candidates))
            {
                var file = await media.LoadAsync(Path.Combine(data.MediaRoot, item.File), cancellationToken).ConfigureAwait(false);
                var still = media.Decoder.Decode(file.Bytes, 0, LongestEdge, Path.GetFileName(item.File));
                var image = (still.LongestEdge > LongestEdge ? media.Decoder.Shrink(still, LongestEdge) : still).ToAiImage(Path.GetFileName(item.File));
                var started = Stopwatch.GetTimestamp();
                var read = await engine.RecognizeTextAsync(image, cancellationToken).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                texts[item.Id] = read.Value.Text;
                output.WriteLine($"  {item.Id} {elapsed:0} ms");
                if (truth.Contains(item.Id))
                {
                    times.Add(elapsed);
                }
                else if (read.Value.HasText)
                {
                    withText.Add($"{item.Id}: {read.Value.Text.ReplaceLineEndings(" / ")}");
                }
            }

            var path = options.Out ?? Path.Combine(data.Directory, "ocr-results", name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ResultsFile.Contents { System = name, Ocr = texts }, EvaluationJson.Options), cancellationToken)
                .ConfigureAwait(false);

            // The first image loads the model, so it's left out of the timing.
            var timed = times.Skip(1).Order().ToList();
            output.WriteLine($"{name}: {items.Count} ground-truth images, {candidates.Count} no-text candidates -> {path}");
            output.WriteLine($"  ms per image: mean {timed.Average():0}, median {timed[timed.Count / 2]:0}, max {timed[^1]:0} (first, with loading: {times[0]:0})");
            output.WriteLine($"  no-text candidates with text: {withText.Count}");
            foreach (var line in withText)
            {
                output.WriteLine($"    {line}");
            }

            return 0;
        }
        finally
        {
            dispose?.Dispose();
        }
    }

    private static (IOcrProvider Engine, string Name, IDisposable? Dispose) CreateEngine(CommandOptions options)
    {
        switch (options.System)
        {
            case "rapidocr":
                var models = options.Models ?? throw new ArgumentException("ocr --system rapidocr needs --models <folder> with the detector and recogniser.");
                var provider = options.ExecutionProvider is { } name
                    ? Enum.Parse<ExecutionProvider>(name, ignoreCase: true)
                    : ExecutionProvider.Cpu;
                var store = new DirectoryModelStore(
                    models,
                    RapidOcrModel.ModelId,
                    RapidOcrModel.Version,
                    "PP-OCRv5 mobile eslav",
                    new Dictionary<string, string>
                    {
                        [RapidOcrModel.DetectorRole] = "ch_PP-OCRv5_det_mobile.onnx",
                        [RapidOcrModel.RecognizerRole] = "eslav_PP-OCRv5_rec_mobile.onnx",
                    });
                var rapidOptions = new RapidOcrOptions { ExecutionProvider = provider };
                rapidOptions = options.DetectionMaxSide is { } maxSide ? rapidOptions with { DetectionMaxSide = maxSide } : rapidOptions;
                var rapid = new RapidOcrProvider(store, new OnnxSessionFactory(), rapidOptions);
                return (rapid, $"rapidocr-{provider.ToString().ToLowerInvariant()}-{rapidOptions.DetectionMaxSide}", rapid);
            case "vlm":
                var model = options.Vlm ?? AnalyzeCommand.DefaultModel;
                var endpoint = new OpenAiCompatibleEndpoint(new Uri(options.Endpoint ?? AnalyzeCommand.DefaultEndpoint), model) { Timeout = TimeSpan.FromMinutes(5) };
                var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                return (new OpenAiCompatibleOcrProvider("eval-ocr", model, endpoint, http), "vlm-" + model.Replace('/', '_'), http);
            default:
                throw new ArgumentException("ocr needs --system rapidocr or --system vlm.");
        }
    }
}
