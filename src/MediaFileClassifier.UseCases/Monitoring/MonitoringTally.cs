using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// What a watched folder's changes did to the library.
/// </summary>
/// <param name="Added">New items.</param>
/// <param name="Modified">Items whose file changed and is checked again.</param>
/// <param name="Moved">Items whose file moved or was renamed.</param>
/// <param name="Missing">Items whose file is gone.</param>
/// <param name="Folder">The folder's path as shown to the user.</param>
public sealed record MonitoringCounts(int Added, int Modified, int Moved, int Missing, string Folder = "")
{
    /// <summary>
    /// Gets the number of changes in all.
    /// </summary>
    public int Total => Added + Modified + Moved + Missing;

    /// <summary>
    /// Adds counts to these.
    /// </summary>
    /// <param name="other">The counts to add.</param>
    /// <returns>The sum.</returns>
    public MonitoringCounts Plus(MonitoringCounts other) =>
        new(Added + other.Added, Modified + other.Modified, Moved + other.Moved, Missing + other.Missing, Folder);
}

/// <summary>
/// Remembers what the monitoring jobs of the open session changed (E04-S06-T03), so the activity log and the
/// notification can tell a monitoring job from an ordinary import and say what it did. A job's batches add up. Only the
/// newest <see cref="Capacity"/> jobs are kept; nothing is stored: an app that quit loses them, and its startup sync
/// reports what it finds.
/// </summary>
public sealed class MonitoringTally
{
    /// <summary>
    /// How many jobs are remembered.
    /// </summary>
    public const int Capacity = 200;

    private readonly Lock syncRoot = new();
    private readonly Dictionary<Id<ProcessingJob>, MonitoringCounts> counts = [];
    private readonly Queue<Id<ProcessingJob>> order = new();

    /// <summary>
    /// Adds what a batch of a monitoring job did.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="batch">The batch's counts.</param>
    public void Add(Id<ProcessingJob> jobId, MonitoringCounts batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        lock (syncRoot)
        {
            if (counts.TryGetValue(jobId, out var existing))
            {
                counts[jobId] = existing.Plus(batch);
                return;
            }

            counts[jobId] = batch;
            order.Enqueue(jobId);
            while (order.Count > Capacity)
            {
                counts.Remove(order.Dequeue());
            }
        }
    }

    /// <summary>
    /// Gets what a job did.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <returns>The counts, or <see langword="null"/> when the job isn't a monitoring job or is forgotten.</returns>
    public MonitoringCounts? Get(Id<ProcessingJob> jobId)
    {
        lock (syncRoot)
        {
            return counts.GetValueOrDefault(jobId);
        }
    }
}
