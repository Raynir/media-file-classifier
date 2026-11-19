using System.Diagnostics;
using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// The describer against a real vision-language model server (E06-S04-T02 Done when: every field is filled for the
/// sample corpus with a model served by LM Studio). Opt-in: set <c>MFC_VLM_SAMPLES</c> to a folder of pictures, e.g. the
/// describer spike's <c>models/describer-spike/inputs</c>; <c>MFC_VLM_ENDPOINT</c> (default LM Studio's
/// <c>http://127.0.0.1:1234/v1</c>) and <c>MFC_VLM_MODEL</c> (default <c>qwen/qwen3-vl-4b</c>) pick the server.
/// Writes <c>%TEMP%\mfc-vlm-describe2.tsv</c> with each answer and its time.
/// </summary>
public sealed class OpenAiCompatibleImageDescriberLiveTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Describe2_FillsEveryField_OverTheSamples()
    {
        var samples = Environment.GetEnvironmentVariable("MFC_VLM_SAMPLES");
        Assert.SkipWhen(string.IsNullOrEmpty(samples), "Set MFC_VLM_SAMPLES to a folder of pictures to run the describer against a server.");
        var endpoint = new Uri(Environment.GetEnvironmentVariable("MFC_VLM_ENDPOINT") is { Length: > 0 } url ? url : "http://127.0.0.1:1234/v1");
        var model = Environment.GetEnvironmentVariable("MFC_VLM_MODEL") is { Length: > 0 } name ? name : "qwen/qwen3-vl-4b";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var describer = new OpenAiCompatibleImageDescriber(
            "live",
            model,
            new OpenAiCompatibleEndpoint(endpoint, model) { Timeout = TimeSpan.FromMinutes(5) },
            http);
        var files = Directory.GetFiles(samples!).Where(IsPicture).Order(StringComparer.Ordinal).ToList();
        var report = new List<string>();
        var seconds = new List<double>();
        var gaps = new List<string>();

        foreach (var file in files)
        {
            var image = new AiImage(await File.ReadAllBytesAsync(file, Token), MimeType(file), Path.GetFileName(file));
            var stopwatch = Stopwatch.StartNew();
            var value = (await describer.DescribeAsync(image, ImageDescriptionOptions.Default, Token)).Value;
            seconds.Add(stopwatch.Elapsed.TotalSeconds);
            var id = Path.GetFileNameWithoutExtension(file);
            var empty = new[]
            {
                ("title_ru", string.IsNullOrWhiteSpace(value.TitleRu)),
                ("tags", value.Tags.Count == 0),
                ("tags_ru", value.TagsRu is null || value.TagsRu.Count < value.Tags.Count),
                ("objects", value.Objects.Count == 0),
                ("scenes", value.Scenes.Count == 0),
                ("actions", value.Actions.Count == 0),
                ("emotions", value.Emotions.Count == 0),
            }.Where(field => field.Item2).Select(field => field.Item1).ToList();
            gaps.AddRange(empty.Select(field => $"{id}:{field}"));
            var tags = string.Join(", ", value.Tags.Select(tag => value.TagsRu?.GetValueOrDefault(tag) is { } ru ? $"{tag}/{ru}" : tag));
            report.Add(string.Join(
                '\t',
                id,
                stopwatch.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
                string.Join(",", empty),
                value.Title,
                value.TitleRu,
                tags,
                string.Join(", ", value.Objects),
                string.Join(", ", value.Scenes),
                string.Join(", ", value.Actions),
                string.Join(", ", value.Emotions),
                value.Summary));
        }

        seconds.Sort();
        report.Insert(0, string.Create(
            CultureInfo.InvariantCulture,
            $"{model}: median {seconds[seconds.Count / 2]:F2} s, mean {seconds.Average():F2} s, n {seconds.Count}; gaps {gaps.Count}: {string.Join(" ", gaps)}"));
        await File.WriteAllLinesAsync(Path.Combine(Path.GetTempPath(), "mfc-vlm-describe2.tsv"), report, Token);
    }

    private static bool IsPicture(string file) =>
        Path.GetExtension(file).ToUpperInvariant() is ".PNG" or ".JPG" or ".JPEG";

    private static string MimeType(string file) =>
        Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
}
