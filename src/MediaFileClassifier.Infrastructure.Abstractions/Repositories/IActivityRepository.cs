using MediaFileClassifier.Domain.Activity;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the activity log of a library (E15-S04).
/// </summary>
public interface IActivityRepository
{
    /// <summary>
    /// Adds an event and, in the same transaction, drops the oldest events beyond <see cref="ActivityRetention.MaxEntries"/>.
    /// </summary>
    /// <param name="activity">The event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the event is stored.</returns>
    Task AddAsync(ActivityEvent activity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of events, newest first.
    /// </summary>
    /// <param name="filter">Which events.</param>
    /// <param name="skip">How many matching events to skip, the events of the pages before.</param>
    /// <param name="pageSize">How many events at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The events, newest first.</returns>
    Task<IReadOnlyList<ActivityEvent>> GetPageAsync(
        ActivityFilter filter,
        int skip,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the events a filter matches.
    /// </summary>
    /// <param name="filter">Which events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of events.</returns>
    Task<int> CountAsync(ActivityFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of events, newest first, together with the number of events the filter matches. Both come from one
    /// snapshot, so the total always agrees with the page, which two separate calls can't promise while events are added.
    /// </summary>
    /// <param name="filter">Which events.</param>
    /// <param name="skip">How many matching events to skip, the events of the pages before.</param>
    /// <param name="pageSize">How many events at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The events, newest first, and how many events match in all.</returns>
    Task<ActivityPageResult> GetPageWithTotalAsync(
        ActivityFilter filter,
        int skip,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A page of the activity log with the number of events the filter matches in all.
/// </summary>
/// <param name="Events">The events of the page, newest first.</param>
/// <param name="Total">How many events match the filter in all.</param>
public sealed record ActivityPageResult(IReadOnlyList<ActivityEvent> Events, int Total);

/// <summary>
/// Which activity events to list.
/// </summary>
/// <param name="Types">Only events of these types, or <see langword="null"/> for every type.</param>
/// <param name="MinimumSeverity">Only events at least this severe, or <see langword="null"/> for all.</param>
public sealed record ActivityFilter(IReadOnlyCollection<ActivityEventType>? Types = null, ActivitySeverity? MinimumSeverity = null)
{
    /// <summary>
    /// Gets the filter that matches every event.
    /// </summary>
    public static ActivityFilter All { get; } = new();
}
