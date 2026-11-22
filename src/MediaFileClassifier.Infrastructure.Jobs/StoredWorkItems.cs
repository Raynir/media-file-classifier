using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Collects the batches of work items a repository write reported stored, so a write that fails part way takes back
/// only the rest from the progress tracker (MFC-1010).
/// </summary>
internal sealed class StoredWorkItems
{
    private readonly Lock gate = new();
    private readonly HashSet<Id<WorkItem>> stored = [];

    /// <summary>
    /// Records a stored batch.
    /// </summary>
    /// <param name="batch">The batch.</param>
    public void Add(IReadOnlyCollection<WorkItem> batch)
    {
        lock (gate)
        {
            foreach (var workItem in batch)
            {
                stored.Add(workItem.Id);
            }
        }
    }

    /// <summary>
    /// Gets the work items that weren't reported stored.
    /// </summary>
    /// <param name="workItems">All the work items of the write.</param>
    /// <returns>Those not stored.</returns>
    public IReadOnlyCollection<WorkItem> NotStored(IReadOnlyCollection<WorkItem> workItems)
    {
        lock (gate)
        {
            return stored.Count == 0 ? workItems : [.. workItems.Where(workItem => !stored.Contains(workItem.Id))];
        }
    }
}
