using System.Text.Json;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Reporting;
using MediaFileClassifier.Evaluation.Systems;

namespace MediaFileClassifier.Evaluation.Tests.Reporting;

/// <summary>
/// Tests for <see cref="Evaluator"/>.
/// </summary>
public sealed class EvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ScoresSearchPerLanguage()
    {
        var report = await Evaluate();

        var search = Assert.IsType<SearchSection>(report.Search);
        var en = search.Languages.Single(l => l.Language == "en");
        var ru = search.Languages.Single(l => l.Language == "ru");
        Assert.Equal(1.0, en.RecallAt10);
        Assert.Equal(0.5, en.Mrr);
        Assert.Equal(0.25, ru.RecallAt10);          // (0.5 + 0) / 2
        Assert.Equal(0.1667, ru.Mrr);               // (1/3 + 0) / 2, rounded
        Assert.Equal(["unknown-id"], search.Queries.Single(q => q.Id == "ru-001").UnknownIds);
        Assert.Equal(0, search.Queries.Single(q => q.Id == "ru-002").Returned);
    }

    [Fact]
    public async Task EvaluateAsync_ScoresNoneQueriesApartFromRecallAndMrr()
    {
        var data = TestData.Data with
        {
            Queries =
            [
                .. TestData.Queries,
                new EvalQuery { Id = "en-002", Language = "en", Category = QueryCategories.None, Text = "a giraffe", Relevant = [] },
                new EvalQuery { Id = "en-003", Language = "en", Category = QueryCategories.None, Text = "asdfghjkl", Relevant = [] },
            ],
        };
        var results = new ResultsFile(TestData.Results with
        {
            Search = new Dictionary<string, IReadOnlyList<string>>(TestData.Results.Search!) { ["en-002"] = ["lynx"] },
        });

        var report = await Evaluator.EvaluateAsync(data, results.Name, results, null, TestData.FirstRun, CancellationToken.None);

        var search = Assert.IsType<SearchSection>(report.Search);
        var en = search.Languages.Single(l => l.Language == "en");
        Assert.Equal((1, 1.0, 0.5, 2, 0.5), (en.Queries, en.RecallAt10, en.Mrr, en.NoneQueries, en.NoneShown));
        Assert.Null(search.Languages.Single(l => l.Language == "ru").NoneShown);
        Assert.Equal(0.5, search.Categories.Single(c => c.Category == QueryCategories.None).NoneShown);
        Assert.Null(search.Categories.Single(c => c.Category == "object" && c.Language == "en").NoneShown);
    }

    [Fact]
    public async Task EvaluateAsync_ScoresOcrPerScriptAndOverall()
    {
        var report = await Evaluate();

        var ocr = Assert.IsType<OcrSection>(report.Ocr);
        Assert.Equal(["cyrillic", "all"], ocr.Scripts.Select(s => s.Script));
        var cyrillic = ocr.Scripts[0];
        Assert.Equal(17, cyrillic.ReferenceCharacters);
        Assert.Equal(2, cyrillic.Edits);
        Assert.Equal(0.1176, cyrillic.Cer);
    }

    [Fact]
    public async Task EvaluateAsync_GivesIdenticalMetricsOnRepeatedRuns()
    {
        var first = await Evaluate();
        var second = await Evaluate(TestData.FirstRun.AddHours(1));

        Assert.Equal(Metrics(first), Metrics(second));
    }

    [Fact]
    public async Task EvaluateAsync_TreatsMissingOcrOutputAsFullyWrong()
    {
        var results = new ResultsFile(TestData.Results with { Ocr = new Dictionary<string, string>() });

        var report = await Evaluator.EvaluateAsync(TestData.Data, results.Name, null, results, TestData.FirstRun, CancellationToken.None);

        var image = Assert.Single(report.Ocr!.Images);
        Assert.True(image.Missing);
        Assert.Equal(1.0, image.Cer);
        Assert.Null(report.Search);
    }

    private static Task<EvaluationReport> Evaluate(DateTimeOffset? at = null)
    {
        var results = new ResultsFile(TestData.Results);
        return Evaluator.EvaluateAsync(TestData.Data, results.Name, results, results, at ?? TestData.FirstRun, CancellationToken.None);
    }

    private static string Metrics(EvaluationReport report) =>
        JsonSerializer.Serialize(new { report.Search, report.Ocr, report.Data }, EvaluationJson.Options);
}
