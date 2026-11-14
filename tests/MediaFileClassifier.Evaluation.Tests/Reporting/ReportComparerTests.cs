using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Reporting;
using MediaFileClassifier.Evaluation.Systems;

namespace MediaFileClassifier.Evaluation.Tests.Reporting;

/// <summary>
/// Tests for <see cref="ReportComparer"/>, <see cref="ReportStore"/> and <see cref="ReportWriter"/>.
/// </summary>
public sealed class ReportComparerTests
{
    [Fact]
    public async Task Compare_WithoutBaselinesOrPreviousRun_ReportsNoBaselineAndNoDelta()
    {
        var report = ReportComparer.Compare(await Evaluate(TestData.Results), null, null, new Baselines());

        Assert.All(report.Comparisons, c => Assert.Equal(ReportComparer.NoBaseline, c.Status));
        Assert.All(report.Comparisons, c => Assert.Null(c.Delta));
        Assert.Null(report.PreviousUsedOtherInputs);
        Assert.Equal(
            ["recall@10 en", "mrr en", "recall@10 ru", "mrr ru", "ocr-cer cyrillic", "ocr-cer all"],
            report.Comparisons.Select(c => $"{c.Metric} {c.Group}"));
    }

    [Fact]
    public async Task Compare_AppliesBaselinesAsMinimumsForSearchAndMaximumsForOcr()
    {
        var baselines = new Baselines
        {
            RecallAt10 = new Dictionary<string, double?> { ["en"] = 0.7, ["ru"] = 0.7 },
            OcrCer = new Dictionary<string, double?> { ["cyrillic"] = 0.2, ["all"] = 0.1 },
        };

        var report = ReportComparer.Compare(await Evaluate(TestData.Results), null, null, baselines);

        Assert.Equal(ReportComparer.Pass, Status(report, "recall@10", "en"));
        Assert.Equal(ReportComparer.Below, Status(report, "recall@10", "ru"));
        Assert.Equal(ReportComparer.NoBaseline, Status(report, "mrr", "en"));
        Assert.Equal(ReportComparer.Pass, Status(report, "ocr-cer", "cyrillic"));
        Assert.Equal(ReportComparer.Below, Status(report, "ocr-cer", "all"));
    }

    [Fact]
    public async Task Compare_ShowsTheChangeFromThePreviousRun()
    {
        var previous = await Evaluate(TestData.Results with
        {
            Search = new Dictionary<string, IReadOnlyList<string>> { ["en-001"] = ["lynx", "sign", "cat-1"] },
        });

        var report = ReportComparer.Compare(await Evaluate(TestData.Results), previous, "old.json", new Baselines());

        var recall = report.Comparisons.Single(c => c.Metric == "recall@10" && c.Group == "en");
        Assert.Equal(0.5, recall.Previous);
        Assert.Equal(0.5, recall.Delta);
        Assert.Equal("old.json", report.Previous);
        Assert.False(report.PreviousUsedOtherInputs);
    }

    [Fact]
    public async Task Compare_NoticesChangedLabels()
    {
        var previous = await Evaluate(TestData.Results);
        previous = previous with { Data = previous.Data with { InputHashes = new Dictionary<string, string> { ["queries.json"] = "old" } } };

        var report = ReportComparer.Compare(await Evaluate(TestData.Results), previous, "old.json", new Baselines());

        Assert.True(report.PreviousUsedOtherInputs);
    }

    [Fact]
    public async Task Store_FindsTheLatestEarlierReportOfTheSameSystem()
    {
        using var temporary = new TemporaryFolder();
        var folder = temporary.Path;
        var first = await Evaluate(TestData.Results);
        var other = (await Evaluate(TestData.Results with { System = "Other" })) with { CreatedAt = TestData.FirstRun.AddMinutes(30) };
        ReportStore.Write(folder, first);
        ReportStore.Write(folder, other);
        File.WriteAllText(Path.Combine(folder, "notes.json"), """{ "x": 1 }""");
        File.WriteAllText(Path.Combine(folder, "list.json"), "[]");
        File.WriteAllText(Path.Combine(folder, "empty.json"), "null");
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{");

        var found = ReportStore.FindPrevious(folder, first.System, TestData.FirstRun.AddHours(1));

        Assert.NotNull(found);
        Assert.Equal("2026-11-01_090000_test-system-1.json", found.Value.Name);
        Assert.Null(ReportStore.FindPrevious(folder, first.System, TestData.FirstRun));
    }

    [Fact]
    public async Task Compare_TreatsNoneShownAsAMaximum_AndTheWriterListsTheQueriesThatShowedResults()
    {
        var data = TestData.Data with
        {
            Queries =
            [
                .. TestData.Queries,
                new EvalQuery { Id = "en-002", Language = "en", Category = QueryCategories.None, Text = "a giraffe", Relevant = [] },
            ],
        };
        var results = new ResultsFile(TestData.Results with
        {
            Search = new Dictionary<string, IReadOnlyList<string>>(TestData.Results.Search!) { ["en-002"] = ["lynx", "sign"] },
        });
        var evaluated = await Evaluator.EvaluateAsync(data, results.Name, results, null, TestData.FirstRun, CancellationToken.None);

        var report = ReportComparer.Compare(evaluated, null, null, new Baselines { NoneShown = new Dictionary<string, double?> { ["en"] = 0.5 } });
        var markdown = ReportWriter.Write(report);

        Assert.Equal(ReportComparer.Below, Status(report, "none-shown", "en"));
        Assert.DoesNotContain(report.Comparisons, c => c.Metric == "none-shown" && c.Group == "ru");
        Assert.Contains("1 of 1 showed results.", markdown, StringComparison.Ordinal);
        Assert.Contains("| en-002 | 2 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| en-002 | none |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_RendersTheSummaryAndWarnings()
    {
        var report = ReportComparer.Compare(await Evaluate(TestData.Results), null, null, new Baselines());

        var markdown = ReportWriter.Write(report);

        Assert.Contains("| recall@10 | ru | 0.2500 | - | - | no baseline |", markdown, StringComparison.Ordinal);
        Assert.Contains("| cyrillic | 1 | 17 | 2 | 0.1176 |", markdown, StringComparison.Ordinal);
        Assert.Contains("1 queries returned nothing; 1 returned IDs are not in the manifest", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", markdown, StringComparison.Ordinal);
    }

    private static Task<EvaluationReport> Evaluate(ResultsFile.Contents contents)
    {
        var results = new ResultsFile(contents);
        return Evaluator.EvaluateAsync(TestData.Data, results.Name, results, results, TestData.FirstRun, CancellationToken.None);
    }

    private static string Status(EvaluationReport report, string metric, string group) =>
        report.Comparisons.Single(c => c.Metric == metric && c.Group == group).Status;
}
