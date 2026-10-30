using System.Globalization;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Engines;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// The options of the <c>run</c> and <c>generate</c> commands.
/// </summary>
internal sealed record BenchmarkOptions
{
    public IReadOnlyList<int> Items { get; init; } = [10_000, 100_000];

    public IReadOnlyList<int> VectorsPerItem { get; init; } = [1, 3];

    public int Dimensions { get; init; } = 512;

    public int Seed { get; init; } = 17;

    public int Queries { get; init; } = 100;

    public int K { get; init; } = 20;

    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(20);

    public IReadOnlyList<string> Engines { get; init; } = EngineCatalog.Names;

    public string DataDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "mfc-vector-bench");

    public string OutputDirectory { get; init; } = Path.GetFullPath(Path.Combine("artifacts", "benchmarks"));

    public bool Rebuild { get; init; }

    public IEnumerable<LibrarySpec> Libraries =>
        from items in Items
        from vectorsPerItem in VectorsPerItem
        select new LibrarySpec(items, vectorsPerItem, Dimensions, Seed);

    public static BenchmarkOptions Parse(IReadOnlyList<string> args)
    {
        var options = new BenchmarkOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            options = name switch
            {
                "--quick" => options with { Items = [2_000], Queries = 20, Budget = TimeSpan.FromSeconds(5) },
                "--rebuild" => options with { Rebuild = true },
                "--items" => options with { Items = Ints(Value(args, ref i, name)) },
                "--vectors-per-item" => options with { VectorsPerItem = Ints(Value(args, ref i, name)) },
                "--dimensions" => options with { Dimensions = Int(Value(args, ref i, name)) },
                "--seed" => options with { Seed = Int(Value(args, ref i, name)) },
                "--queries" => options with { Queries = Int(Value(args, ref i, name)) },
                "--k" => options with { K = Int(Value(args, ref i, name)) },
                "--budget-seconds" => options with { Budget = TimeSpan.FromSeconds(Int(Value(args, ref i, name))) },
                "--engines" => options with { Engines = Value(args, ref i, name).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                "--data-dir" => options with { DataDirectory = Path.GetFullPath(Value(args, ref i, name)) },
                "--out" => options with { OutputDirectory = Path.GetFullPath(Value(args, ref i, name)) },
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
        }

        foreach (var engine in options.Engines)
        {
            if (!EngineCatalog.Names.Contains(engine))
            {
                throw new ArgumentException($"Unknown engine '{engine}'. Known: {string.Join(", ", EngineCatalog.Names)}.");
            }
        }

        if (options.Queries + 3 > LibrarySpec.StoredQueries)
        {
            throw new ArgumentException($"--queries can be at most {LibrarySpec.StoredQueries - 3}.");
        }

        return options;
    }

    internal static string Value(IReadOnlyList<string> args, ref int index, string name)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"{name} needs a value.");
        }

        return args[++index];
    }

    internal static int Int(string value) => int.Parse(value.Replace("_", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

    private static int[] Ints(string value) => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Int)];
}
