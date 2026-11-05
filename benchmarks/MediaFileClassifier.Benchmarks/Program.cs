using MediaFileClassifier.Benchmarks.Checks;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Running;

namespace MediaFileClassifier.Benchmarks;

/// <summary>
/// The vector search benchmark harness (MFC-17). See README.md for the commands.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run -c Release --project benchmarks/MediaFileClassifier.Benchmarks -- [command] [options]

        Commands:
          run        Generate the libraries if needed, run every engine on them, write a report (default).
          generate   Only generate the libraries.
          check      Extension loading, filter and transaction checks on a small library.
          production Benchmark the app's search index on a copy of a real library (E01-S04-T07).

        Options (run, generate):
          --items 10000,100000       Item counts.
          --vectors-per-item 1,3     Vectors per item (3 = keyframes and model migration budget).
          --dimensions 512           Embedding dimension.
          --queries 100              Queries per filter scenario.
          --k 20                     Results per query.
          --budget-seconds 20        Time limit per engine and filter scenario.
          --engines a,b              Engines to run (default: all).
          --data-dir <path>          Where generated libraries are kept (default: %TEMP%\mfc-vector-bench).
          --out <path>               Where reports go (default: artifacts\benchmarks).
          --rebuild                  Regenerate libraries even if they exist.
          --quick                    A small smoke run: 2,000 items, 20 queries.
        """;

    public static int Main(string[] args)
    {
        try
        {
            var command = args.Length > 0 && (!args[0].StartsWith("--", StringComparison.Ordinal) || args[0] == "--help") ? args[0] : "run";
            var rest = command == "run" && (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)) ? args : args[1..];
            switch (command)
            {
                case "run":
                    return BenchmarkRunner.Run(BenchmarkOptions.Parse(rest));
                case "generate":
                    var options = BenchmarkOptions.Parse(rest);
                    foreach (var spec in options.Libraries)
                    {
                        LibraryGenerator.Ensure(spec, options.DataDirectory, options.Rebuild, Console.Out);
                    }

                    return 0;
                case "check":
                    return SpikeChecks.Run(BenchmarkOptions.Parse(rest).DataDirectory);
                case "production":
                    return Production.ProductionBenchmark.Run(rest);
                case "worker":
                    return Worker.Run(WorkerOptions.Parse(rest));
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
    }
}
