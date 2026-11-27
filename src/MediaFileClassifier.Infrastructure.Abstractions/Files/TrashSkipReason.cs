namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// Why <see cref="IFileTrash"/> left a file where it is.
/// </summary>
public enum TrashSkipReason
{
    /// <summary>The file wasn't skipped.</summary>
    None,

    /// <summary>There is no file at the path.</summary>
    NotFound,

    /// <summary>The path is a folder, or not a full path to a file.</summary>
    NotAFile,

    /// <summary>The file, or its drive, is read-only.</summary>
    ReadOnly,

    /// <summary>The file's location has no trash (e.g. a network share or some removable drives), or the file is too
    /// big for it. Deleting it would be permanent, so it's refused (D024).</summary>
    NoTrash,

    /// <summary>The operation was cancelled before the file was reached.</summary>
    Cancelled,
}
