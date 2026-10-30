using System.Globalization;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// The arguments the runner passes to a worker process.
/// </summary>
internal sealed record WorkerOptions(
    string DatabasePath,
    string Library,
    string Engine,
    int Dimensions,
    int VectorsPerItem,
    int Queries,
    int K,
    TimeSpan Budget,
    string ResultPath)
{
    public IEnumerable<string> ToArguments() =>
    [
        "worker",
        DatabasePath,
        Library,
        Engine,
        Invariant(Dimensions),
        Invariant(VectorsPerItem),
        Invariant(Queries),
        Invariant(K),
        Invariant((int)Budget.TotalSeconds),
        ResultPath,
    ];

    public static WorkerOptions Parse(IReadOnlyList<string> args) => new(
        args[0],
        args[1],
        args[2],
        BenchmarkOptions.Int(args[3]),
        BenchmarkOptions.Int(args[4]),
        BenchmarkOptions.Int(args[5]),
        BenchmarkOptions.Int(args[6]),
        TimeSpan.FromSeconds(BenchmarkOptions.Int(args[7])),
        args[8]);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
