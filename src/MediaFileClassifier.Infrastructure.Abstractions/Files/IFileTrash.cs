namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// Moves files to the system trash, e.g. the Windows Recycle Bin (D024, E12-S04-T08). It never deletes a file
/// permanently: a file that can't go to the trash, because it's read-only or its location has no trash (some network
/// shares and removable drives), is skipped and reported, and stays where it is.
/// </summary>
public interface IFileTrash
{
    /// <summary>
    /// Moves files to the trash, one at a time, and reports what happened to each.
    /// </summary>
    /// <param name="paths">Full paths of the files. Folders are not trashed.</param>
    /// <param name="cancellationToken">Stops before the next file. The files not reached are reported as skipped
    /// (<see cref="TrashSkipReason.Cancelled"/>), so the result still names every path; the call doesn't throw.</param>
    /// <returns>One result per path, in the order given.</returns>
    Task<IReadOnlyList<TrashResult>> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
