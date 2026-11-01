namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// Matches files against the items of one library (E01-S06-T05), for moved and renamed files (E04), metadata import
/// (E07), duplicates (E12) and phone libraries (E20). Create one with <see cref="IFileMatcherFactory"/> per pass,
/// e.g. per rescan or import: the perceptual and file name tiers use a snapshot taken when it was created.
/// </summary>
public interface IFileMatcher
{
    /// <summary>
    /// Matches a file. The tiers are tried in order, and the first that finds anything wins: content hash,
    /// fingerprint and size, perceptual hash (images only), then file name and metadata.
    /// </summary>
    /// <param name="file">What's known about the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match, or <see cref="FileMatch.None"/>.</returns>
    Task<FileMatch> MatchAsync(FileFacts file, CancellationToken cancellationToken = default);
}
