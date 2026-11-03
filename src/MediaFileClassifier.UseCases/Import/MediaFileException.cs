namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// A problem with a file that an import stage found itself, e.g. a file that can't be decoded at all.
/// </summary>
public sealed class MediaFileException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaFileException"/> class.
    /// </summary>
    public MediaFileException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaFileException"/> class.
    /// </summary>
    /// <param name="message">What's wrong, shown to the user.</param>
    public MediaFileException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaFileException"/> class.
    /// </summary>
    /// <param name="message">What's wrong, shown to the user.</param>
    /// <param name="innerException">The cause.</param>
    public MediaFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the file itself is the problem, so a retry waits until the file changes.
    /// </summary>
    public bool IsPermanent { get; init; } = true;
}
