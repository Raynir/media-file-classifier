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

        Options:
          --data <folder>        The data set (default: eval, the public set).
          --results <file>       evaluate: the system's results (see docs/evaluation-corpus.md).
          --system siglip2       evaluate: run the app's SigLIP 2 pipeline over the corpus instead of reading a results file.
          --models <folder>      evaluate --system: the folder with vision_model.onnx, text_model.onnx and tokenizer.model.
          --out <folder>         evaluate: where reports go (default: <data>/reports).
          --media <folder>       The media files (default: from <data>/dataset.json, else <data>/corpus).
          --files                validate: also check that every file exists and matches its checksum.
          --fail-below-baseline  evaluate: exit with code 3 when a metric is worse than its baseline.
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
