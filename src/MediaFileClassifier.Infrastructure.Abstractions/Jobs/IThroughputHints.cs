namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// A throughput known before a job has run, e.g. from the benchmark (E06-S08-T04). The progress tracker uses it for
/// the estimated time remaining until enough work items have finished to measure the real rate (E06-S06-T03).
/// </summary>
public interface IThroughputHints
{
    /// <summary>
    /// Gets the work items per second to expect from a job type.
    /// </summary>
    /// <param name="jobType">The job's type, e.g. <c>analysis</c>.</param>
    /// <returns>The rate, or <see langword="null"/> when nothing is known.</returns>
    double? ItemsPerSecond(string jobType);
}
