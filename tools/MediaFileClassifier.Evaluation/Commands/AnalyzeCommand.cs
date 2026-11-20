using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Runs the app's describer and OCR over the corpus on an OpenAI-compatible endpoint (E09-S05) and stores what they
/// wrote, so keyword and hybrid evaluations index the same text every time. Like the app's analysis
/// (<c>ItemAnalyzers</c>): a still image is described at 1024 px and read at 2048 px on its longest edge; a video or
/// animated image is described from its middle keyframe and read on every keyframe, keeping the distinct lines.
/// </summary>
/// <remarks>
/// The file is saved after every item, and a second run skips the items already in it, so an interrupted run resumes.
/// </remarks>
internal static class AnalyzeCommand
{
    public const string DefaultEndpoint = "http://localhost:1234/v1";

    public const string DefaultModel = "qwen/qwen3-vl-4b";

    private const int DescribeLongestEdge = 1024;

    private const int OcrLongestEdge = 2048;

    public static async Task<int> RunAsync(CommandOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var data = EvaluateCommand.Load(options);
        var model = options.Vlm ?? DefaultModel;
        var path = options.Analysis ?? DefaultPath(data.Directory, model);
        var analysis = File.Exists(path) ? CorpusAnalysis.Load(path) : new CorpusAnalysis { Model = model, Language = options.Language };
        if (analysis.Model != model || analysis.Language != options.Language)
        {
            throw new ArgumentException($"{path} holds {analysis.Model} in '{analysis.Language}'; give another --analysis file for {model} in '{options.Language}'.");
        }

        var endpoint = new OpenAiCompatibleEndpoint(new Uri(options.Endpoint ?? DefaultEndpoint), model) { Timeout = TimeSpan.FromMinutes(5) };
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var describer = new OpenAiCompatibleImageDescriber("eval-describer", model, endpoint, http);
        var ocr = new OpenAiCompatibleOcrProvider("eval-ocr", model, endpoint, http);
        var ocrFrom = options.OcrFrom is { } ocrPath ? CorpusAnalysis.Load(ocrPath) : null;
        using var media = new CorpusMedia();
        analysis = analysis with { PromptVersion = describer.PromptVersion };

        var todo = data.Items.Where(item => !analysis.Items.ContainsKey(item.Id)).ToList();
        output.WriteLine($"Analysing {todo.Count} of {data.Items.Count} items with {model} into {path}");
        var failed = 0;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < todo.Count; i++)
        {
            var item = todo[i];
            try
            {
                var file = await media.LoadAsync(Path.Combine(data.MediaRoot, item.File), cancellationToken).ConfigureAwait(false);
                var storedOcr = ocrFrom?.Items.GetValueOrDefault(item.Id);
                var result = await AnalyseAsync(file, Path.GetFileName(item.File), media.Decoder, describer, storedOcr is null ? ocr : null, options.Language, cancellationToken).ConfigureAwait(false);
                if (result is not null && storedOcr is not null)
                {
                    result = result with { OcrText = storedOcr.OcrText };
                }

                if (result is null)
                {
                    failed++;
                    output.WriteLine($"  {item.Id}: no frames (is FFmpeg installed?)");
                    continue;
                }

                analysis.Items[item.Id] = result;
                analysis.Save(path);
            }
            catch (AiProviderException exception)
            {
                failed++;
                output.WriteLine($"  {item.Id}: {exception.Message}");
            }

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            output.WriteLine($"  {i + 1}/{todo.Count} {item.Id} ({elapsed.TotalMinutes:0.0} min)");
        }

        output.WriteLine(failed == 0 ? "Done." : $"Done; {failed} items failed. Run the command again to retry them.");
        return failed == 0 ? 0 : 1;
    }

    public static string DefaultPath(string dataDirectory, string model) =>
        Path.Combine(dataDirectory, "analysis", model.Replace('/', '_').Replace(':', '_') + ".json");

    private static async Task<ItemAnalysis?> AnalyseAsync(
        CorpusMediaFile file,
        string name,
        IAnalysisImageDecoder decoder,
        IImageDescriber describer,
        IOcrProvider? ocr,
        string language,
        CancellationToken cancellationToken)
    {
        AiImage describeImage;
        string ocrText;
        if (file.Frames is { } frames)
        {
            if (frames.Count == 0)
            {
                return null;
            }

            describeImage = frames[frames.Count / 2];
            var texts = new List<string?>(frames.Count);
            foreach (var frame in ocr is null ? [] : frames)
            {
                var read = await ocr!.RecognizeTextAsync(frame, cancellationToken).ConfigureAwait(false);
                texts.Add(read.Value.HasText ? read.Value.Text : null);
            }

            ocrText = string.Join('\n', KeyframeAggregation.MergeTextLines(texts));
        }
        else
        {
            var still = decoder.Decode(file.Bytes, 0, OcrLongestEdge, name);
            var forOcr = still.LongestEdge > OcrLongestEdge ? decoder.Shrink(still, OcrLongestEdge) : still;
            describeImage = (still.LongestEdge > DescribeLongestEdge ? decoder.Shrink(still, DescribeLongestEdge) : still).ToAiImage(name);
            var read = ocr is null ? null : await ocr.RecognizeTextAsync(forOcr.ToAiImage(name), cancellationToken).ConfigureAwait(false);
            ocrText = read is { Value.HasText: true } ? read.Value.Text : string.Empty;
        }

        // This tool runs with invariant globalization, where the describer can't look up a language's name from its
        // code; the app can, so give it the name the app's prompt would have.
        var languageName = language switch
        {
            "en" => "English",
            "ru" => "Russian",
            _ => language,
        };
        var described = (await describer.DescribeAsync(describeImage, new ImageDescriptionOptions(languageName), cancellationToken).ConfigureAwait(false)).Value;
        return new ItemAnalysis
        {
            Title = Blank(described.Title),
            Description = Blank(described.Description),
            Summary = Blank(described.Summary),
            Tags = described.Tags,
            Objects = described.Objects,
            Scenes = described.Scenes,
            Actions = described.Actions,
            Emotions = described.Emotions,
            OcrText = Blank(ocrText),
            TitleRu = Blank(described.TitleRu),
            TagsRu = described.TagsRu is { Count: > 0 } forms ? new SortedDictionary<string, string>(forms.ToDictionary(), StringComparer.Ordinal) : null,
        };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
