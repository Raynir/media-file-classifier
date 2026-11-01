using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Systems;

namespace MediaFileClassifier.Evaluation.Tests;

/// <summary>
/// A small private data set and results for it, built in memory or written to a temporary folder.
/// </summary>
internal static class TestData
{
    public static readonly DateTimeOffset FirstRun = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<CorpusItem> Items { get; } =
    [
        new() { Id = "cat-1", Kind = "photo", File = "cat-1.jpg" },
        new() { Id = "cat-2", Kind = "photo", File = "cat-2.jpg" },
        new() { Id = "lynx", Kind = "photo", File = "lynx.jpg" },
        new() { Id = "sign", Kind = "photo", File = "sign.jpg" },
    ];

    public static IReadOnlyList<EvalQuery> Queries { get; } =
    [
        new() { Id = "en-001", Language = "en", Category = "object", Text = "a cat", Relevant = ["cat-1", "cat-2"] },
        new() { Id = "ru-001", Language = "ru", Category = "object", Text = "кошка", Relevant = ["cat-1", "cat-2"] },
        new() { Id = "ru-002", Language = "ru", Category = "text", Text = "пивная библиотека", Relevant = ["sign"] },
    ];

    public static IReadOnlyList<OcrTruthEntry> OcrTruth { get; } =
    [
        new() { Id = "sign", Scripts = ["cyrillic"], Origin = "transcribed", Text = "ПИВНАЯ\nБИБЛИОТЕКА" },
    ];

    public static EvaluationData Data => new()
    {
        Directory = Path.GetTempPath(),
        Info = new DataSetInfo { Name = "test", Private = true },
        Items = Items,
        Queries = Queries,
        OcrTruth = OcrTruth,
        InputHashes = new Dictionary<string, string> { ["queries.json"] = "abc" },
    };

    /// <summary>
    /// en-001 finds both cats (first at rank 2); ru-001 finds one cat at rank 3; ru-002 finds nothing.
    /// The OCR output has two wrong letters out of 17.
    /// </summary>
    public static ResultsFile.Contents Results => new()
    {
        System = "Test System 1",
        Search = new Dictionary<string, IReadOnlyList<string>>
        {
            ["en-001"] = ["lynx", "cat-1", "cat-2"],
            ["ru-001"] = ["lynx", "sign", "cat-2", "unknown-id"],
        },
        Ocr = new Dictionary<string, string> { ["sign"] = "ПИВHАЯ БИБЛИОТЕКA" },
    };

    /// <summary>
    /// Writes the data set, a results file and a dataset.json to a new temporary folder.
    /// </summary>
    public static string WriteFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mfc-eval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        EvaluationJson.WriteLines(Path.Combine(folder, EvaluationData.ManifestFile), Items);
        EvaluationJson.WriteLines(Path.Combine(folder, EvaluationData.QueriesFile), Queries);
        EvaluationJson.WriteLines(Path.Combine(folder, EvaluationData.OcrTruthFile), OcrTruth);
        File.WriteAllText(Path.Combine(folder, EvaluationData.DataSetFile), """{ "name": "test", "mediaRoot": "media", "private": true }""");
        File.WriteAllText(Path.Combine(folder, "results.json"), System.Text.Json.JsonSerializer.Serialize(Results, EvaluationJson.Options));
        return folder;
    }
}
