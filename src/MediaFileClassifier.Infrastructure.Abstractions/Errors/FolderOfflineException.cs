namespace MediaFileClassifier.Infrastructure.Abstractions.Errors;

/// <summary>
/// Thrown by a stage when the item's indexed folder is Offline (D188): its root can't be listed, e.g. an unplugged drive
/// or a deleted folder. The scheduler parks the work until the folder is Online again, without using up a retry or
/// marking the item failed (D025).
/// </summary>
public sealed class FolderOfflineException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderOfflineException"/> class.
    /// </summary>
    public FolderOfflineException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderOfflineException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public FolderOfflineException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderOfflineException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure that showed the folder is gone, if any.</param>
    public FolderOfflineException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception for a folder.
    /// </summary>
    /// <param name="folderPath">The folder's display path.</param>
    /// <param name="innerException">The failure that showed the folder is gone, if any.</param>
    /// <returns>The exception.</returns>
    public static FolderOfflineException For(string folderPath, Exception? innerException = null) =>
        new($"The folder {folderPath} is offline.", innerException);
}
