using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// Creates <see cref="IFileMatcher"/>s for an open library.
/// </summary>
public interface IFileMatcherFactory
{
    /// <summary>
    /// Creates a matcher. It reads the items' perceptual hashes and file names once, so create it at the start of a
    /// pass and drop it at the end.
    /// </summary>
    /// <param name="items">The media items of the library to match against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matcher.</returns>
    Task<IFileMatcher> CreateAsync(IMediaItemRepository items, CancellationToken cancellationToken = default);
}
