namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// What <see cref="IFileTrash"/> did with one file.
/// </summary>
/// <param name="Path">The path, as given.</param>
/// <param name="Outcome">Whether the file was moved, skipped or failed.</param>
/// <param name="SkipReason">Why the file was skipped; <see cref="TrashSkipReason.None"/> unless it was.</param>
/// <param name="Error">What went wrong, for a failed file; otherwise <see langword="null"/>.</param>
public sealed record TrashResult(string Path, TrashOutcome Outcome, TrashSkipReason SkipReason = TrashSkipReason.None, string? Error = null)
{
    /// <summary>
    /// Makes the result of a file moved to the trash.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The result.</returns>
    public static TrashResult Moved(string path) => new(path, TrashOutcome.Moved);

    /// <summary>
    /// Makes the result of a file left in place, on purpose.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    public static TrashResult Skipped(string path, TrashSkipReason reason) => new(path, TrashOutcome.Skipped, reason);

    /// <summary>
    /// Makes the result of a file the trash couldn't take because of an error, e.g. it's open in another program.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="error">What went wrong.</param>
    /// <returns>The result.</returns>
    public static TrashResult Failed(string path, string error) => new(path, TrashOutcome.Failed, Error: error);
}
