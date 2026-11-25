using MediaFileClassifier.Domain.Activity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Testing.Activity;

/// <summary>
/// An activity log kept in memory (E15-S04). It lists newest first, like the real one, and can be made to fail.
/// </summary>
public sealed class InMemoryActivityRepository : IActivityRepository
{
    /// <summary>
    /// Gets the stored events, in the order they were added.
    /// </summary>
    public List<ActivityEvent> Events { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether adding an event throws.
    /// </summary>
    public bool Fails { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether adding an event throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public bool Cancels { get; set; }

    /// <inheritdoc />
    public Task AddAsync(ActivityEvent activity, CancellationToken cancellationToken = default)
    {
        if (Cancels)
        {
            throw new OperationCanceledException();
        }

        if (Fails)
        {
            throw new InvalidOperationException("The log is not writable.");
        }

        Events.Add(activity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActivityEvent>> GetPageAsync(ActivityFilter filter, int skip, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityEvent>>([.. Matching(filter).OrderByDescending(activity => activity.OccurredAt).Skip(skip).Take(pageSize)]);

    /// <inheritdoc />
    public Task<int> CountAsync(ActivityFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(Matching(filter).Count());

    /// <inheritdoc />
    public async Task<ActivityPageResult> GetPageWithTotalAsync(ActivityFilter filter, int skip, int pageSize, CancellationToken cancellationToken = default) =>
        new(await GetPageAsync(filter, skip, pageSize, cancellationToken), await CountAsync(filter, cancellationToken));

    private IEnumerable<ActivityEvent> Matching(ActivityFilter filter) => Events.Where(activity =>
        (filter.Types is null || filter.Types.Contains(activity.Type))
        && (filter.MinimumSeverity is null || activity.Severity >= filter.MinimumSeverity));
}
