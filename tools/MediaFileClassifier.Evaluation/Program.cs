using MediaFileClassifier.Evaluation.Commands;

namespace MediaFileClassifier.Evaluation;

/// <summary>
/// The evaluation runner (E01-S10). See docs/evaluation-corpus.md.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/MediaFileClassifier.Evaluation -- <command> [options]

        Commands:
          validate       Check the data set: IDs, labels, minimums and licences.
          evaluate       Score a results file and write a dated report.
          init-private   List a folder of personal media into a new private data set.
          analyze        Describe and OCR the corpus with a vision model, for keyword and hybrid evaluations.
          tune           Score a fixed grid of ranking settings of the app's search over the corpus (E09-S05).
          ocr            Run one OCR engine over the OCR ground truth and the photos without text (E06-S05).
          threshold      Measure SigLIP 2 similarities of real and nonsense queries, for the relevance threshold.

        Options:
          --data <folder>        The data set (default: eval, the public set).
          --results <file>       evaluate: the system's results (see docs/evaluation-corpus.md).
          --system <name>        evaluate: run a system over the corpus instead of reading a results file: siglip2 (vector
                                 similarity alone), or the app's search: app-hybrid, app-vector, app-keyword.
          --rrf-k <k>            evaluate app-*: the RRF constant (default 60).
          --keyword-weight <w>   evaluate app-*: the keyword list's RRF weight (default 1).
          --vector-weight <w>    evaluate app-*: the vector list's RRF weight (default 1).
          --field-weights <list> evaluate app-*: BM25 field weights, e.g. title=4,tags=3,filename=2 (fields: title,
                                 description, tags, ocr, transcript, other, filename).
          --threshold <c,r|none> evaluate app-*: the relevance cut, centred and raw similarity (default 0.07,0.13).
          --models <folder>      evaluate --system, threshold: the folder with vision_model.onnx, text_model.onnx and tokenizer.model.
          --out <folder>         evaluate: where reports go (default: <data>/reports).
          --media <folder>       The media files (default: from <data>/dataset.json, else <data>/corpus).
          --files                validate: also check that every file exists and matches its checksum.
          --fail-below-baseline  evaluate: exit with code 3 when a metric is worse than its baseline.
          --endpoint <url>       analyze: the OpenAI-compatible endpoint (default: http://localhost:1234/v1, LM Studio).
          --vlm <model>          analyze, evaluate: the vision model (default: qwen/qwen3-vl-4b).
          --analysis <file>      analyze, evaluate: the stored analysis (default: <data>/analysis/<vlm>.json).
          --language <code>      analyze: the language the describer writes in (default: en).
          --det-max-side <px>    ocr --system rapidocr: the longest side the text detector sees (default 960).
          --ep <cpu|directml>    ocr --system rapidocr: the execution provider (DirectML needs a -p:OnnxGpu=true build).
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.Length > 0 ? args[0] : "help";
            var options = args.Length > 1 ? CommandOptions.Parse(args[1..]) : new CommandOptions();
            switch (command)
            {
                case "validate":
                    return ValidateCommand.Run(options, Console.Out);
                case "evaluate":
                    return await EvaluateCommand.RunAsync(options, TimeProvider.System, Console.Out, CancellationToken.None);
                case "analyze":
                    return await AnalyzeCommand.RunAsync(options, Console.Out, CancellationToken.None);
                case "tune":
                    return await TuneCommand.RunAsync(options, TimeProvider.System, Console.Out, CancellationToken.None);
                case "ocr":
                    return await OcrCommand.RunAsync(options, Console.Out, CancellationToken.None);
                case "threshold":
                    return await ThresholdCommand.RunAsync(options, Console.Out, CancellationToken.None);
                case "init-private":
                    return InitPrivateCommand.Run(options, Console.Out);
                default:
                    Console.WriteLine(Usage);
                    return command is "help" or "-h" or "--help" ? 0 : 2;
            }
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
