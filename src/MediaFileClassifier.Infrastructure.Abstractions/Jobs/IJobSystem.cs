using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// Ties the job scheduler to the open library's lifetime (E05-S01-T05, T06, T07). Whoever opens and closes libraries
/// calls <see cref="StartAsync"/> after opening one and <see cref="StopAsync"/> before disposing it, so jobs never
/// write to a closed library.
/// </summary>
public interface IJobSystem
{
    /// <summary>
    /// Gets the scheduler of the open library, or <see langword="null"/> when no library is open.
    /// </summary>
    IJobScheduler? Current { get; }

    /// <summary>
    /// Starts processing for a library that was just opened. Work left running by a crash or by quitting is put back
    /// in the queue first. Processing resumes at once, or waits for <see cref="IJobScheduler.Resume"/> when the
    /// settings say to ask. A scheduler that's still running for another library is stopped first.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The library's scheduler.</returns>
    Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing before the library closes: no new work starts, running work gets 2 seconds to finish, and
    /// what's still running then is cancelled and put back in the queue. Does nothing when no library is open.
    /// </summary>
    /// <returns>A task that completes when no work is running and nothing more will be written.</returns>
    Task StopAsync();

    /// <summary>
    /// Counts the work items still to do in the open library, e.g. for the quit confirmation (D027).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of queued and running work items, or 0 when no library is open.</returns>
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);
}
