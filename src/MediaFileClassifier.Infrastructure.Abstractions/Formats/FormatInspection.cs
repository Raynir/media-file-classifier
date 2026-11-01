using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// What a <see cref="IMediaFormatHandler"/> found inside a file.
/// </summary>
/// <param name="MediaType">The kind of media, or <c>null</c> when the file isn't supported.</param>
/// <param name="Rejection">Why the file isn't supported, or <c>null</c> when it is.</param>
public sealed record FormatInspection(MediaType? MediaType, MediaFormatIssue? Rejection)
{
    /// <summary>
    /// Creates the result for a supported file.
    /// </summary>
    /// <param name="mediaType">The kind of media.</param>
    /// <returns>The result.</returns>
    public static FormatInspection Supported(MediaType mediaType) => new(mediaType, null);

    /// <summary>
    /// Creates the result for a file that has the format's signature but isn't supported, such as an audio-only MP4.
    /// </summary>
    /// <param name="rejection">Why it isn't supported.</param>
    /// <returns>The result.</returns>
    public static FormatInspection Rejected(MediaFormatIssue rejection) => new(null, rejection);
}
