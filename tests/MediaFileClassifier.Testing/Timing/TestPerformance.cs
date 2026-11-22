namespace MediaFileClassifier.Testing.Timing;

/// <summary>
/// Switches the performance bounds of tests on (MFC-1025). A bound on elapsed time measures the machine as much as the
/// code: test projects run in parallel, and other workers' builds and test runs share the CPU, so a plain
/// <c>dotnet test</c> keeps every correctness assertion of such a test and only reports its timings. With
/// <see cref="Variable"/> set to <c>1</c>, the bounds are asserted as well; run those tests alone then
/// (docs/testing.md, "Timing tests and parallelism").
/// </summary>
public static class TestPerformance
{
    /// <summary>
    /// The environment variable that switches the bounds on.
    /// </summary>
    public const string Variable = "MFC_PERF_TESTS";

    /// <summary>
    /// Gets a value indicating whether performance bounds are asserted.
    /// </summary>
    public static bool Enabled => Environment.GetEnvironmentVariable(Variable) == "1";
}
