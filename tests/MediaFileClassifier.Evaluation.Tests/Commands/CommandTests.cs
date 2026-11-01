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
        var folder = TestData.WriteFolder();
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
    public async Task Evaluate_FailBelowBaseline_ReturnsThree()
    {
        var folder = TestData.WriteFolder();
        File.WriteAllText(Path.Combine(folder, EvaluationData.BaselinesFile), """{ "recallAt10": { "ru": 0.9 } }""");
        var options = CommandOptions.Parse(["--data", folder, "--results", Path.Combine(folder, "results.json"), "--fail-below-baseline"]);

        Assert.Equal(3, await EvaluateCommand.RunAsync(options, new FixedClock(TestData.FirstRun), TextWriter.Null, CancellationToken.None));
    }

    [Fact]
    public async Task Evaluate_RefusesAnInconsistentDataSet()
    {
        var folder = TestData.WriteFolder();
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
        var root = Path.Combine(Path.GetTempPath(), "mfc-eval-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
