namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// The supported formats (D010) and format detection (E03-S01). The formats come from the registered
/// <see cref="IMediaFormatHandler"/> services.
/// </summary>
public interface IMediaFormatRegistry
{
    /// <summary>
    /// Gets the registered handlers.
    /// </summary>
    IReadOnlyList<IMediaFormatHandler> Handlers { get; }

    /// <summary>
    /// Checks whether a file's extension is registered, the first filter before its content is read.
    /// </summary>
    /// <param name="fileName">File name or path.</param>
    /// <returns><see langword="true"/> when the file should be read and detected.</returns>
    bool IsCandidate(string fileName);

    /// <summary>
    /// Finds the handler of a format.
    /// </summary>
    /// <param name="formatId">Format id, such as <c>jpeg</c>.</param>
    /// <returns>The handler, or <see langword="null"/> when the format isn't registered.</returns>
    IMediaFormatHandler? FindByFormatId(string formatId);

    /// <summary>
    /// Detects a file's format from its content. The extension only selects which files are read: content that matches
    /// another registered format is accepted with a warning, and content that matches none is rejected. A bad file
    /// never throws; read errors become a rejection.
    /// </summary>
    /// <param name="content">The file content, positioned at the start. It may not be seekable.</param>
    /// <param name="fileName">File name or path, for the extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detected format, or why the file is skipped.</returns>
    Task<MediaFormatDetection> DetectAsync(Stream content, string fileName, CancellationToken cancellationToken);
}
