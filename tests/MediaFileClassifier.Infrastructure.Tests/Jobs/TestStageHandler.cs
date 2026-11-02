using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.Infrastructure.Tests.Jobs;

/// <summary>
/// A stage handler for scheduler tests. It records the work items it ran, in order, and how many ran at once.
/// </summary>
/// <param name="stage">Stage key.</param>
/// <param name="pool">Resource pool.</param>
/// <param name="work">What to do for each work item; by default nothing.</param>
internal sealed class TestStageHandler(string stage, ResourcePool pool, Func<WorkItemContext, CancellationToken, Task>? work = null) : IStageHandler
{
    private int concurrent;
    private int maxConcurrent;

    public string Stage => stage;

    public ResourcePool Pool => pool;

    /// <summary>
    /// Gets the work items that ran, in the order they started.
    /// </summary>
    public ConcurrentQueue<WorkItem> Started { get; } = new();

    /// <summary>
    /// Gets the most work items that ran at once.
    /// </summary>
    public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

    /// <summary>
    /// Gets whether every work item ran on a thread-pool thread.
    /// </summary>
    public bool AlwaysOnThreadPool { get; private set; } = true;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        Started.Enqueue(context.WorkItem);
        AlwaysOnThreadPool &= Thread.CurrentThread.IsThreadPoolThread;
        var now = Interlocked.Increment(ref concurrent);
        int seen;
        while ((seen = Volatile.Read(ref maxConcurrent)) < now && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
        {
        }

        try
        {
            if (work is not null)
            {
                await work(context, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref concurrent);
        }
    }
}
