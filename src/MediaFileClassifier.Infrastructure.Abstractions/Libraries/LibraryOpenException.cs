namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Thrown when a library can't be created or opened. The message is for the log; the UI shows <see cref="Error"/>.
/// </summary>
public sealed class LibraryOpenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryOpenException"/> class.
    /// </summary>
    /// <param name="error">Why the library couldn't be opened.</param>
    /// <param name="libraryDirectory">The library folder.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public LibraryOpenException(LibraryOpenError error, string libraryDirectory, Exception? innerException = null)
        : base($"Library {libraryDirectory} can't be opened: {error}.", innerException)
    {
        Error = error;
        LibraryDirectory = libraryDirectory;
    }

    /// <summary>
    /// Gets why the library couldn't be opened.
    /// </summary>
    public LibraryOpenError Error { get; }

    /// <summary>
    /// Gets the library folder.
    /// </summary>
    public string LibraryDirectory { get; }
}
