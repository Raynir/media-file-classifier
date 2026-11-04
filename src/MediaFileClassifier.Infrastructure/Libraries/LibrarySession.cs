using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Libraries;

/// <summary>
/// Keeps the open library and closes it when another one replaces it. Background processing (E05) follows the open
/// library: it stops before a library closes, and starts, resuming interrupted work, when one opens.
/// </summary>
/// <param name="jobs">The job system, or <c>null</c> where nothing is processed in the background.</param>
public sealed class LibrarySession(IJobSystem? jobs = null) : ILibrarySession, IAsyncDisposable
{
    /// <inheritdoc />
    public event EventHandler? CurrentChanged;

    /// <inheritdoc />
    public IOpenLibrary? Current { get; private set; }

    /// <inheritdoc />
    public async Task SetCurrentAsync(IOpenLibrary? library)
    {
        var previous = Current;
        if (ReferenceEquals(previous, library))
        {
            return;
        }

        Current = library;
        try
        {
            if (previous is not null)
            {
                // Nothing may write to a library once it's closed.
                if (jobs is not null)
                {
                    await jobs.StopAsync().ConfigureAwait(false);
                }

                await previous.DisposeAsync().ConfigureAwait(false);
            }

            if (library is not null && jobs is not null)
            {
                await jobs.StartAsync(library).ConfigureAwait(false);
            }
        }
        finally
        {
            // Current has changed even if closing the previous library failed.
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Closes the open library when the app shuts down.
    /// </summary>
    /// <returns>A task that completes when the library is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Current is { } library)
        {
            Current = null;
            if (jobs is not null)
            {
                await jobs.StopAsync().ConfigureAwait(false);
            }

            await library.DisposeAsync().ConfigureAwait(false);
        }
    }
}
