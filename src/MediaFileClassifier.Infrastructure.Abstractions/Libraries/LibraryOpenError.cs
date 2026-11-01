namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Why a library couldn't be created or opened, as a code rather than text (E13-S08). The UI maps it to a resource
/// string.
/// </summary>
public enum LibraryOpenError
{
    /// <summary>
    /// The library folder or its database doesn't exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// A folder with the new library's name already exists and isn't empty.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// Another app instance has the library open.
    /// </summary>
    InUse,

    /// <summary>
    /// A newer app version created or upgraded the library.
    /// </summary>
    NewerVersion,

    /// <summary>
    /// The database isn't a library database, e.g. its library record is missing.
    /// </summary>
    NotALibrary,

    /// <summary>
    /// The library folder can't be written, e.g. it's read-only, the user has no write permission, or the disk failed.
    /// An open library needs to write its lock file and database.
    /// </summary>
    NotWritable,
}
