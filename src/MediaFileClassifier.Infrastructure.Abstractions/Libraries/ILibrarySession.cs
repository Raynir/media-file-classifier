namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// The library this app instance works on (E02). At most one library is open at a time.
/// </summary>
public interface ILibrarySession
{
    /// <summary>
    /// Raised after <see cref="Current"/> changed.
    /// </summary>
    event EventHandler? CurrentChanged;

    /// <summary>
    /// Gets the open library, or <c>null</c> if none is open.
    /// </summary>
    IOpenLibrary? Current { get; }

    /// <summary>
    /// Makes a library the open one, closing the one open before.
    /// </summary>
    /// <param name="library">The newly opened library, or <c>null</c> to close the current one.</param>
    /// <returns>A task that completes when the previous library is closed.</returns>
    Task SetCurrentAsync(IOpenLibrary? library);
}
