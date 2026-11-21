namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// Measures how much disk space a library's own files take (E02-S05-T03): the database, the thumbnail cache and the
/// backups. The files are walked on a background thread, and the result is cached per library.
/// </summary>
public interface ILibraryDiskUsage
{
    /// <summary>
    /// Gets a library's disk usage: the cached value when it's younger than <paramref name="maxAge"/>, else a new
    /// measurement. Callers asking while a measurement runs share it.
    /// </summary>
    /// <param name="layout">Where the library's files are.</param>
    /// <param name="maxAge">How old a cached value may be.</param>
    /// <param name="cancellationToken">Stops waiting; the measurement itself still finishes and is cached.</param>
    /// <returns>The disk usage.</returns>
    Task<LibraryDiskUsage> MeasureAsync(LibraryLayout layout, TimeSpan maxAge, CancellationToken cancellationToken = default);
}
