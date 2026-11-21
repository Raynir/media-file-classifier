namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Deletes a library's own files (E02-S07). Only the files the app writes into a library folder are deleted, by exact
/// name pattern; anything else in the folder, such as the user's media or notes, stays. Deletion is permanent. See
/// "Deleting a library" in <c>docs/library-storage.md</c>.
/// </summary>
public interface ILibraryDeleter
{
    /// <summary>
    /// Reads what the confirmation shows about a closed library: its name and item count. The lock is taken while it's
    /// read, so a library open in another app instance is reported. A <c>.mfc</c> folder whose database is gone but
    /// which still has library files, after a delete that was cut short, is reported as
    /// <see cref="LibraryDeletionPreview.IsPartial"/>, named after its folder.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The library's name and item count.</returns>
    /// <exception cref="LibraryOpenException">The library is missing (<see cref="LibraryOpenError.NotFound"/>), open in
    /// another instance (<see cref="LibraryOpenError.InUse"/>), not a library, or its folder isn't writable.</exception>
    Task<LibraryDeletionPreview> InspectAsync(string libraryDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the known files of a closed library, then the library folder if nothing else is left in it. The caller
    /// closes the library first. Once the database is deleted the rest is deleted too, whatever the token says. A partly
    /// deleted library (see <see cref="InspectAsync"/>) has its remaining known files deleted.
    /// </summary>
    /// <param name="libraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
    /// <param name="cancellationToken">Cancellation token, honoured until deleting starts.</param>
    /// <returns>What was left in the folder.</returns>
    /// <exception cref="LibraryOpenException">The library is missing, open in this or another instance
    /// (<see cref="LibraryOpenError.InUse"/>), or its database can't be deleted (<see cref="LibraryOpenError.NotWritable"/>);
    /// nothing was deleted.</exception>
    Task<LibraryDeletionResult> DeleteAsync(string libraryDirectory, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the delete confirmation shows about a library.
/// </summary>
/// <param name="Name">The library's name.</param>
/// <param name="ItemCount">How many items the library has indexed, excluded ones included.</param>
public sealed record LibraryDeletionPreview(string Name, int ItemCount)
{
    /// <summary>
    /// Gets a value indicating whether the library's database is already gone and only its other files are left.
    /// </summary>
    public bool IsPartial { get; init; }
}

/// <summary>
/// What deleting a library left behind.
/// </summary>
/// <param name="FolderRemoved">Whether the library folder itself was removed, because nothing else was in it.</param>
/// <param name="KeptEntries">The entries left in the library folder, relative to it: files that aren't the library's,
/// and library files that couldn't be deleted. Empty when the folder was removed.</param>
public sealed record LibraryDeletionResult(bool FolderRemoved, IReadOnlyList<string> KeptEntries);
