using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Libraries;

/// <summary>
/// Keeps the open library and closes it when another one replaces it.
/// </summary>
public sealed class LibrarySession : ILibrarySession, IAsyncDisposable
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
        if (previous is not null)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
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
            await library.DisposeAsync().ConfigureAwait(false);
        }
    }
}
