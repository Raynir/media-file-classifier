using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// Job progress set by the test.
/// </summary>
internal sealed class FakeJobProgress : IJobProgress
{
    private ProgressSummary current = ProgressSummary.Empty;

    public event EventHandler? Changed;

    public event EventHandler<JobFinishedEventArgs>? JobFinished;

    public int Reads { get; private set; }

    public ProgressSummary Current
    {
        get
        {
            Reads++;
            return current;
        }
    }

    /// <summary>
    /// Changes the progress and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="summary">The new progress.</param>
    public void Set(ProgressSummary summary)
    {
        current = summary;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="JobFinished"/>.
    /// </summary>
    /// <param name="args">What the job did.</param>
    public void Finish(JobFinishedEventArgs args) => JobFinished?.Invoke(this, args);
}
