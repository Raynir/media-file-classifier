using MediaFileClassifier.Evaluation.Commands;
using MediaFileClassifier.Evaluation.Reporting;

namespace MediaFileClassifier.Evaluation.Tests.Commands;

/// <summary>
/// The embedding quality regression test (E06-S03-T05): runs the app's SigLIP 2 pipeline over the public evaluation
/// corpus and checks it against the baselines in <c>eval/baselines.json</c> (D094). It needs the model files and the
/// fetched corpus, which CI doesn't have, so it skips there. It takes about two minutes. See
/// <c>docs/evaluation-corpus.md</c> for running it by hand.
/// </summary>
public sealed class Siglip2QualityTests
{
    [Fact]
    public async Task Evaluate_AppSiglip2Pipeline_MeetsTheBaselines()
    {
        var (eval, models) = FindInputs();
        using var temporary = new TemporaryFolder();
        var options = CommandOptions.Parse(["--data", eval, "--system", "siglip2", "--models", models, "--out", temporary.Path, "--fail-below-baseline"]);
        var output = new StringWriter();

        var exitCode = await EvaluateCommand.RunAsync(options, TimeProvider.System, output, TestContext.Current.CancellationToken);

        TestContext.Current.SendDiagnosticMessage(output.ToString());
        var report = ReportStore.Read(Directory.GetFiles(temporary.Path, "*.json").Single())!;
        Assert.Equal(0, exitCode);
        Assert.NotEmpty(report.Comparisons);
        Assert.DoesNotContain("have no vector", output.ToString(), StringComparison.Ordinal);
    }

    private static (string Eval, string Models) FindInputs()
    {
        var models = Environment.GetEnvironmentVariable("MFC_SIGLIP2_DIR");
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var eval = Path.Combine(directory.FullName, "eval");
            if (Directory.Exists(Path.Combine(eval, "corpus")))
            {
                models ??= Path.Combine(directory.FullName, "models", "siglip2");
                Assert.SkipUnless(File.Exists(Path.Combine(models, "text_model.onnx")), "The SigLIP 2 model isn't in models/siglip2 or MFC_SIGLIP2_DIR.");
                return (eval, models);
            }
        }

        Assert.Skip("The evaluation corpus isn't fetched (eval/corpus).");
        return default;
    }
}
