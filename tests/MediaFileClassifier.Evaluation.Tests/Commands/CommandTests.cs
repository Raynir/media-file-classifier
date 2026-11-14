using MediaFileClassifier.Evaluation.Commands;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Reporting;

namespace MediaFileClassifier.Evaluation.Tests.Commands;

/// <summary>
/// End-to-end tests of the runner commands on a private data set in a temporary folder.
/// </summary>
public sealed class CommandTests
{
    [Fact]
    public async Task Evaluate_TwiceOnTheSameInput_GivesIdenticalNumbersAndAZeroChange()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        var options = CommandOptions.Parse(["--data", folder, "--results", Path.Combine(folder, "results.json")]);

        Assert.Equal(0, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun), TextWriter.Null, CancellationToken.None));
        Assert.Equal(0, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun.AddDays(1)), TextWriter.Null, CancellationToken.None));

        var reports = Directory.GetFiles(Path.Combine(folder, "reports"), "*.json").Order(StringComparer.Ordinal).Select(p => ReportStore.Read(p)!).ToList();
        Assert.Equal(2, reports.Count);
        Assert.Equal(reports[0].Comparisons.Select(c => c.Value), reports[1].Comparisons.Select(c => c.Value));
        Assert.Equal("2026-11-01_090000_test-system-1.json", reports[1].Previous);
        Assert.All(reports[1].Comparisons, c => Assert.Equal(0.0, c.Delta));
        Assert.Contains("Previous run: 2026-11-01_090000_test-system-1.json", File.ReadAllText(Path.ChangeExtension(Directory.GetFiles(Path.Combine(folder, "reports"), "*.json").Max(StringComparer.Ordinal)!, ".md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_WithAPinnedBaselineRun_ShowsTheChangeFromItOnEveryRun()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        var results = Path.Combine(folder, "results.json");
        var options = CommandOptions.Parse(["--data", folder, "--results", results]);
        Assert.Equal(0, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun), TextWriter.Null, CancellationToken.None));
        File.WriteAllText(Path.Combine(folder, EvaluationData.BaselinesFile), """{ "references": { "Test System 1": "2026-11-01_090000_test-system-1" } }""");

        // A better second run: ru-001 now finds both cats first.
        var better = TestData.Results with
        {
            Search = new Dictionary<string, IReadOnlyList<string>>(TestData.Results.Search!) { ["ru-001"] = ["cat-1", "cat-2"] },
        };
        File.WriteAllText(results, System.Text.Json.JsonSerializer.Serialize(better, EvaluationJson.Options));
        using var output = new StringWriter();
        Assert.Equal(0, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun.AddDays(1)), output, CancellationToken.None));

        var latest = Directory.GetFiles(Path.Combine(folder, "reports"), "*.json").Max(StringComparer.Ordinal)!;
        var report = ReportStore.Read(latest)!;
        var recall = report.Comparisons.Single(c => c.Metric == "recall@10" && c.Group == "ru");
        Assert.Equal("2026-11-01_090000_test-system-1.json", report.Reference);
        Assert.Equal((0.25, 0.5, 0.25), (recall.Reference, recall.Value, recall.ReferenceDelta));
        Assert.Contains("recall@10  ru        0.5000  +0.2500  no baseline", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("| recall@10 | ru | 0.5000 | 0.2500 | +0.2500 | +0.2500 | - | no baseline |", File.ReadAllText(Path.ChangeExtension(latest, ".md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_FailBelowBaseline_ReturnsThree()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        File.WriteAllText(Path.Combine(folder, EvaluationData.BaselinesFile), """{ "recallAt10": { "ru": 0.9 } }""");
        var options = CommandOptions.Parse(["--data", folder, "--results", Path.Combine(folder, "results.json"), "--fail-below-baseline"]);

        Assert.Equal(3, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun), TextWriter.Null, CancellationToken.None));
    }

    [Fact]
    public async Task Evaluate_RefusesAnInconsistentDataSet()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        File.WriteAllText(Path.Combine(folder, EvaluationData.QueriesFile), """[{ "id": "en-001", "language": "en", "category": "object", "text": "a dog", "relevant": ["dog"] }]""");
        var output = new StringWriter();

        var code = await EvaluateCommand.RunAsync(
            CommandOptions.Parse(["--data", folder, "--results", Path.Combine(folder, "results.json")]), new FixedClock(TestData.FirstRun), output, CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("points at unknown item dog", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder, "reports")));
    }

    [Fact]
    public void InitPrivate_ListsTheMediaAndMarksTheSetPrivate()
    {
        using var temporary = new TemporaryFolder();
        var root = temporary.Path;
        var media = Path.Combine(root, "Photos");
        Directory.CreateDirectory(Path.Combine(media, "2025 Отпуск"));
        File.WriteAllText(Path.Combine(media, "2025 Отпуск", "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(media, "clip.mp4"), "mp4");
        File.WriteAllText(Path.Combine(media, "notes.txt"), "skip me");
        var data = Path.Combine(root, "private-set");

        var code = InitPrivateCommand.Run(CommandOptions.Parse(["--data", data, "--media", media]), TextWriter.Null);

        Assert.Equal(0, code);
        var loaded = EvaluationData.Load(data);
        Assert.True(loaded.Info.Private);
        Assert.Equal(Path.GetFullPath(media), loaded.MediaRoot);
        Assert.Equal(["2025 Отпуск/IMG_0001.JPG", "clip.mp4"], loaded.Items.Select(i => i.File));
        Assert.Equal(["photo", "video"], loaded.Items.Select(i => i.Kind));
        Assert.Empty(DataValidator.Validate(loaded, checkFiles: true));
    }

    [Fact]
    public void InitPrivate_RequiresBothFolders()
    {
        using var temporary = new TemporaryFolder();

        Assert.Throws<ArgumentException>(() => InitPrivateCommand.Run(CommandOptions.Parse(["--media", temporary.Path]), TextWriter.Null));
        Assert.Throws<ArgumentException>(() => InitPrivateCommand.Run(CommandOptions.Parse(["--data", temporary.Path]), TextWriter.Null));
    }

    [Fact]
    public void Validate_MediaOption_ChecksFilesInAnotherFolder()
    {
        using var temporary = new TemporaryFolder();
        var media = temporary.Combine("Photos");
        Directory.CreateDirectory(media);
        File.WriteAllText(Path.Combine(media, "a.jpg"), "jpeg");
        var data = temporary.Combine("set");
        InitPrivateCommand.Run(CommandOptions.Parse(["--data", data, "--media", media]), TextWriter.Null);
        var moved = temporary.Combine("Moved");
        Directory.Move(media, moved);
        var output = new StringWriter();

        Assert.Equal(1, ValidateCommand.Run(CommandOptions.Parse(["--data", data, "--files"]), output));
        Assert.Equal(0, ValidateCommand.Run(CommandOptions.Parse(["--data", data, "--files", "--media", moved]), output));
        Assert.Contains("files match", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_TwiceInTheSameSecond_KeepsBothReports()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        var options = CommandOptions.Parse(["--data", folder, "--results", Path.Combine(folder, "results.json")]);

        await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun), TextWriter.Null, CancellationToken.None);
        await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun.AddMilliseconds(400)), TextWriter.Null, CancellationToken.None);

        Assert.Equal(
            ["2026-11-01_090000_test-system-1-2.json", "2026-11-01_090000_test-system-1.json"],
            Directory.GetFiles(Path.Combine(folder, "reports"), "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("2026-11-01_090000_test-system-1.json", ReportStore.Read(Path.Combine(folder, "reports", "2026-11-01_090000_test-system-1-2.json"))!.Previous);
    }

    [Fact]
    public void Load_WithoutDataSetFile_ExplainsWhatIsMissing()
    {
        using var temporary = new TemporaryFolder();
        var folder = TestData.WriteFolder(temporary);
        File.Delete(Path.Combine(folder, EvaluationData.DataSetFile));

        var exception = Assert.Throws<FileNotFoundException>(() => EvaluationData.Load(folder));

        Assert.Contains("dataset.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"private\": true", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
