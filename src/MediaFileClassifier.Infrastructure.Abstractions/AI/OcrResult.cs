namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Text found in an image: the common output of every OCR engine (E06-S05-T01).
/// </summary>
/// <param name="Text">The text, with line breaks between lines. Empty if the image has no text.</param>
/// <param name="Language">Detected language as an ISO 639-1 code, if the provider reports it.</param>
public sealed record OcrResult(string Text, string? Language = null)
{
    /// <summary>
    /// Gets a value indicating whether any text was found.
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Gets where each piece of text is, for engines that locate it (an in-process detector); empty otherwise.
    /// </summary>
    public IReadOnlyList<OcrRegion> Regions { get; init; } = [];
}

/// <summary>
/// One piece of text an OCR engine located, usually a line or a word.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Confidence">The engine's confidence, 0 to 1.</param>
/// <param name="Corners">
/// The four corners of the text's box, clockwise from the top left, as fractions (0 to 1) of the image's width and
/// height, so they fit any size of the image.
/// </param>
public sealed record OcrRegion(string Text, double Confidence, IReadOnlyList<OcrPoint> Corners);

/// <summary>
/// A point in an image, as fractions (0 to 1) of its width and height.
/// </summary>
/// <param name="X">From the left edge.</param>
/// <param name="Y">From the top edge.</param>
public readonly record struct OcrPoint(float X, float Y);
