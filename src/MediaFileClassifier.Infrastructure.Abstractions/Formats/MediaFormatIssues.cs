namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// The <see cref="MediaFormatIssue"/> codes.
/// </summary>
public static class MediaFormatIssues
{
    /// <summary>
    /// The extension isn't registered, so the file wasn't read. Detail: the extension.
    /// </summary>
    public const string UnsupportedExtension = nameof(UnsupportedExtension);

    /// <summary>
    /// The file is empty.
    /// </summary>
    public const string EmptyFile = nameof(EmptyFile);

    /// <summary>
    /// The content matches no registered signature, such as a text file named <c>.jpg</c>.
    /// </summary>
    public const string UnrecognizedContent = nameof(UnrecognizedContent);

    /// <summary>
    /// A warning: the content is a supported format, but not the one the extension says. Detail: the detected format.
    /// </summary>
    public const string ExtensionMismatch = nameof(ExtensionMismatch);

    /// <summary>
    /// The container is a relative of a supported format that the MVP doesn't support (D010), such as QuickTime or
    /// HEIF. Detail: the brand.
    /// </summary>
    public const string UnsupportedContainer = nameof(UnsupportedContainer);

    /// <summary>
    /// A video container without a video track, such as an audio-only MP4.
    /// </summary>
    public const string NoVideoTrack = nameof(NoVideoTrack);

    /// <summary>
    /// The structure the format needs is missing or broken, such as an MP4 without a <c>moov</c> box. Detail: what is wrong.
    /// </summary>
    public const string DamagedFile = nameof(DamagedFile);

    /// <summary>
    /// Reading the file failed. Detail: the error message.
    /// </summary>
    public const string Unreadable = nameof(Unreadable);
}
