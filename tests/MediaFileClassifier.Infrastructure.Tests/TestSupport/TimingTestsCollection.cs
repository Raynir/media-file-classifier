namespace MediaFileClassifier.Infrastructure.Tests.TestSupport;

/// <summary>
/// Collection of tests whose pass or fail depends on elapsed time (a latency budget, a short wait). xunit runs
/// test classes in parallel, so under a full run they compete with everything else for the CPU. Classes in this
/// collection run alone (docs/testing.md).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingTestsCollection
{
    /// <summary>
    /// The collection name, for <c>[Collection(TimingTestsCollection.Name)]</c>.
    /// </summary>
    public const string Name = "Timing tests";
}
