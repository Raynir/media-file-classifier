using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// The format of a file, detected from its content (E03-S01).
/// </summary>
public sealed record MediaFormatDetection
{
    /// <summary>
    /// Gets the handler of the detected format, or <c>null</c> when the content matched none.
    /// </summary>
    public IMediaFormatHandler? Handler { get; init; }

    /// <summary>
    /// Gets the kind of media, or <c>null</c> when the file isn't supported.
    /// </summary>
    public MediaType? MediaType { get; init; }

    /// <summary>
    /// Gets why the file is skipped, or <c>null</c> when it is supported.
    /// </summary>
    public MediaFormatIssue? Rejection { get; init; }

    /// <summary>
    /// Gets what is odd about a supported file, such as an extension that doesn't match the content.
    /// </summary>
    public IReadOnlyList<MediaFormatIssue> Warnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the file should be imported.
    /// </summary>
    public bool IsSupported => Rejection is null;

    /// <summary>
    /// Creates the result for a supported file.
    /// </summary>
    /// <param name="handler">Handler of the detected format.</param>
    /// <param name="mediaType">The kind of media.</param>
    /// <param name="warnings">What is odd about the file.</param>
    /// <returns>The result.</returns>
    public static MediaFormatDetection Supported(IMediaFormatHandler handler, MediaType mediaType, IReadOnlyList<MediaFormatIssue> warnings)
        => new() { Handler = handler, MediaType = mediaType, Warnings = warnings };

    /// <summary>
    /// Creates the result for a skipped file.
    /// </summary>
    /// <param name="rejection">Why it is skipped.</param>
    /// <param name="handler">Handler of the detected format, if the signature matched one.</param>
    /// <returns>The result.</returns>
    public static MediaFormatDetection Rejected(MediaFormatIssue rejection, IMediaFormatHandler? handler = null)
        => new() { Handler = handler, Rejection = rejection };
}
